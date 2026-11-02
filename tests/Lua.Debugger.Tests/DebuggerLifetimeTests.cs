using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Lua.Debugging;
using Lua.Standard;

namespace Lua.Debugger.Tests;

public class DebuggerLifetimeTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void FinishedSteppingCoroutineIsNotRetainedByTheDebugger(bool cancelYield)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        using var debugger = new LuaDebugger(state);
        debugger.Attach();
        debugger.Stopped += stop => debugger.Continue(stop.Id, LuaDebugStep.Out);
        var reference = CompleteSteppingCoroutine(state, debugger, cancelYield);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.That(reference.IsAlive, Is.False);
        GC.KeepAlive(state);
        GC.KeepAlive(debugger);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference CompleteSteppingCoroutine(
        LuaState state,
        LuaDebugger debugger,
        bool cancelYield
    )
    {
        using var lifetime = new CancellationTokenSource();
        var thread = state.CreateCoroutine(
            state.Load(
                cancelYield ? "coroutine.yield(1)\nreturn 42" : "return 42",
                "=completed-step"
            )
        );
        debugger.Pause();
        var output = new Lua.Runtime.LuaStack();
        thread.ResumeAsync(output, lifetime.Token).GetAwaiter().GetResult();
        Assert.That(
            output.AsSpan().ToArray(),
            Is.EqualTo(new LuaValue[] { true, cancelYield ? 1 : 42 })
        );
        if (cancelYield)
            lifetime.Cancel();
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead));
        return new WeakReference(thread);
    }

    [Test]
    public void CollectedChunksDoNotAccumulateDebuggerMetadata()
    {
        using var state = LuaState.Create();
        using var debugger = new LuaDebugger(state);
        debugger.Attach();
        var retained = state.Load("return 42", "=generated");
        state.ExecuteAsync(retained).GetAwaiter().GetResult();
        for (var i = 0; i < 16; i++)
        {
            ExecuteTemporaryChunks(state);
            GC.Collect();
        }
        var references = (ICollection)
            typeof(LuaDebugger)
                .GetField("codeReferences", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(debugger)!;
        Assert.That(references.Count, Is.LessThan(512));
        var stops = 0;
        debugger.Stopped += stop =>
        {
            stops++;
            debugger.Continue(stop.Id);
        };
        debugger.SetBreakpoints("=generated", [1]);
        Assert.That(
            state.ExecuteAsync(retained).GetAwaiter().GetResult(),
            Is.EqualTo(new LuaValue[] { 42 })
        );
        Assert.That(stops, Is.EqualTo(1));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void ExecuteTemporaryChunks(LuaState state)
    {
        for (var i = 0; i < 128; i++)
            state.DoStringAsync("return 1", "=generated").GetAwaiter().GetResult();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void YieldedCoroutineDoesNotRetainReleasedDebugger(bool dispose)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var reference = AttachAndYield(state, dispose);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.That(reference.IsAlive, Is.False);
        Assert.That(
            state.DoStringAsync("return coroutine.resume(co)").AsTask().GetAwaiter().GetResult(),
            Is.EqualTo(new LuaValue[] { true, 42 })
        );
        GC.KeepAlive(state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference AttachAndYield(LuaState state, bool dispose)
    {
        var debugger = new LuaDebugger(state);
        debugger.Attach();
        var result = state
            .DoStringAsync(
                "co=coroutine.create(function() coroutine.yield(1);return 42 end);return coroutine.resume(co)"
            )
            .AsTask()
            .GetAwaiter()
            .GetResult();
        Assert.That(result, Is.EqualTo(new LuaValue[] { true, 1 }));
        if (dispose)
            debugger.Dispose();
        else
            debugger.Detach();
        return new WeakReference(debugger);
    }
}
