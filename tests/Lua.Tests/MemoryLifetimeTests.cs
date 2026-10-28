using System.Runtime.CompilerServices;
using Lua.Internal;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

public class MemoryLifetimeTests
{
    [Test]
    public void ClosedCaptureDoesNotRootOriginatingState()
    {
        var (closure, reference) = CreateClosedCapture();
        var upValue = closure.UpValues[0];
        Assert.That(upValue.IsClosed, Is.True);
        Assert.That(upValue.Thread, Is.Null);
        AssertCollected(reference);
        upValue.Close();
        using var state = LuaState.Create();
        Assert.That(
            state.ExecuteAsync(closure).AsTask().GetAwaiter().GetResult()[0],
            Is.EqualTo(new LuaValue(42))
        );
        upValue.SetValue(43);
        Assert.That(upValue.GetValue(), Is.EqualTo(new LuaValue(43)));
        GC.KeepAlive(closure);
    }

    [Test]
    public void DiscardedCompilationDoesNotRootSource()
    {
        AssertCollected(CompileAndDiscard());
    }

    [Test]
    public void ClearedTableDoesNotRootMigratedValue()
    {
        AssertCollected(MigrateAndClearTable());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DisposedPooledListDoesNotRootValues(bool pop)
    {
        AssertCollected(AddAndDispose(pop));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PooledListGrowthPreservesAliasedInputAndReleasesOldBuffer(bool range)
    {
        AssertCollected(GrowAndClear(range));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CompletedTailCallDoesNotRootCapturedValue(bool asynchronous)
    {
        using var state = LuaState.Create();
        var reference = RunTailCall(state, asynchronous);
        AssertCollected(reference);
        state.DoStringAsync("return 42").AsTask().GetAwaiter().GetResult();
        GC.KeepAlive(state);
    }

    [Test]
    public void ClearedExceptionTraceDoesNotRootDiscardedFrames()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var reference = HandleError(state);
        state.DoStringAsync("return 42").AsTask().GetAwaiter().GetResult();
        AssertCollected(reference);
        GC.KeepAlive(state);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HandledErrorDoesNotRootPayloadWhileStateIsIdle(bool text)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        AssertCollected(HandleError(state, text));
        GC.KeepAlive(state);
    }

    [TestCase("clear")]
    [TestCase("pop")]
    [TestCase("pop-range")]
    [TestCase("try-pop")]
    public void RemovedReversedStackValuesAreCollectible(string operation)
    {
        var stack = new ReversedStack<Payload>();
        AssertCollected(RemoveReversedStackValue(stack, operation));
        GC.KeepAlive(stack);
    }

    static void AssertCollected(WeakReference reference)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.That(reference.IsAlive, Is.False);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static (LuaClosure, WeakReference) CreateClosedCapture()
    {
        using var state = LuaState.Create();
        var closure = state
            .DoStringAsync("local x=42; return function() return x end")
            .AsTask()
            .GetAwaiter()
            .GetResult()[0]
            .Read<LuaClosure>();
        return (closure, new WeakReference(state));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference CompileAndDiscard()
    {
        var source = "--" + new string('x', 65536) + "\nreturn 42";
        using var state = LuaState.Create();
        state.DoStringAsync(source).AsTask().GetAwaiter().GetResult();
        return new WeakReference(source);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference MigrateAndClearTable()
    {
        var payload = new object();
        var table = new LuaTable(0, 0);
        table[17] = LuaValue.FromObject(payload);
        for (var i = 1; i <= 16; i++)
            table[i] = i;
        Assert.That(table[17].Read<object>(), Is.SameAs(payload));
        table.Clear();
        return new WeakReference(payload);
    }

    sealed class Payload;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference RunTailCall(LuaState state, bool asynchronous)
    {
        var payload = new object();
        state.Environment["payload"] = LuaValue.FromObject(payload);
        state.Environment["sink"] = new LuaFunction(
            async (context, _) =>
            {
                if (asynchronous)
                    await Task.Yield();
                return context.Return(42);
            }
        );
        state
            .DoStringAsync(
                "local p=payload; payload=nil; local function f() return sink(p) end; return f()"
            )
            .AsTask()
            .GetAwaiter()
            .GetResult();
        return new WeakReference(payload);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference HandleError(LuaState state, bool text = false)
    {
        object payload = text ? new string('x', 65536) : new object();
        state.Environment["payload"] = LuaValue.FromObject(payload);
        state
            .DoStringAsync(
                "local p=payload; payload=nil; local ok=pcall(function() error(p) end); return ok"
            )
            .AsTask()
            .GetAwaiter()
            .GetResult();
        return new WeakReference(payload);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference RemoveReversedStackValue(ReversedStack<Payload> stack, string operation)
    {
        var payload = new Payload();
        stack.Push(payload);
        switch (operation)
        {
            case "clear":
                stack.Clear();
                break;
            case "pop":
                stack.Pop();
                break;
            case "pop-range":
                stack.Pop(1);
                break;
            case "try-pop":
                stack.TryPop(out _);
                break;
        }
        return new WeakReference(payload);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference AddAndDispose(bool pop)
    {
        using var list = new PooledList<Payload>(16);
        var payload = new Payload();
        list.Add(payload);
        if (pop)
            list.Pop(1);
        return new WeakReference(payload);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference GrowAndClear(bool range)
    {
        using var list = new PooledList<Payload>(16);
        var payload = new Payload();
        for (var i = 0; i < 16; i++)
            list.Add(payload);
        if (range)
            list.AddRange(list.AsSpan());
        else
            list.Add(in list.AsSpan()[0]);
        Assert.That(list.Count, Is.EqualTo(range ? 32 : 17));
        for (var i = 0; i < list.Count; i++)
            Assert.That(list[i], Is.SameAs(payload));
        list.Clear();
        return new WeakReference(payload);
    }
}
