using System.Runtime.CompilerServices;
using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class CoroutineEntryLifetimeTests(LuaStringMode mode)
{
    [Test]
    [Combinatorial]
    public void CompletedCoroutineReleasesEntryCaptures(
        [Values(false, true)] bool wrap,
        [Values(false, true)] bool asynchronous,
        [Values(false, true)] bool yieldFirst
    )
    {
        using var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        var reference = RunToCompletion(state, wrap, asynchronous, yieldFirst);
        Assert.That(IsAliveAfterCollection(reference), Is.False);
        var thread = state.Environment["thread"].Read<LuaState>();
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead));

        var again = state
            .DoStringAsync(wrap ? "return pcall(run)" : "return run()")
            .AsTask()
            .GetAwaiter()
            .GetResult();
        Assert.That(again[0], Is.EqualTo(new LuaValue(false)));
        Assert.That(again[1].Read<string>(), Does.Contain("cannot resume dead coroutine"));
        Assert.That(thread.GetStackValues().Length, Is.Zero);
        Assert.That(thread.CallStackFrameCount, Is.Zero);
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
        GC.KeepAlive(thread);
        GC.KeepAlive(state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference RunToCompletion(
        LuaState state,
        bool wrap,
        bool asynchronous,
        bool yieldFirst
    )
    {
        var payload = new object();
        state.Environment["payload"] = LuaValue.FromObject(payload);
        var reference = new WeakReference(payload);
        var suspension = new ManualSuspension();
        state.Environment["wrap"] = wrap;
        state.Environment["yieldFirst"] = yieldFirst;
        state.Environment["pause"] = new LuaFunction(
            async (context, _) =>
            {
                if (asynchronous)
                    await suspension;
                return context.Return();
            }
        );
        var operation = state
            .DoStringAsync(
                """
                local function make(value)
                    return function()
                        thread=coroutine.running()
                        pause()
                        if yieldFirst then coroutine.yield(7) end
                        return value and 42
                    end
                end
                local entry=make(payload)
                payload=nil
                if wrap then
                    run=coroutine.wrap(entry)
                else
                    local co=coroutine.create(entry)
                    run=function() return coroutine.resume(co) end
                end
                entry=nil
                if yieldFirst then run() end
                return run()
                """
            )
            .AsTask();
        if (asynchronous)
            suspension.Resume();
        var result = operation.GetAwaiter().GetResult();
        Assert.That(result, Is.EqualTo(wrap ? new LuaValue[] { 42 } : new LuaValue[] { true, 42 }));
        return reference;
    }

    static bool IsAliveAfterCollection(WeakReference reference)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return reference.IsAlive;
    }
}
