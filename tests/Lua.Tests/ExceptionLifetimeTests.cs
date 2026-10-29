using System.Runtime.CompilerServices;
using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class ExceptionLifetimeTests(LuaStringMode mode)
{
    LuaState CreateState()
    {
        var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        return state;
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void DiscardedExecutionErrorDoesNotRootPayload(bool asynchronous, bool readMessage)
    {
        using var state = CreateState();
        AssertReleased(state, FailExecution(state, asynchronous, readMessage));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference FailExecution(LuaState state, bool asynchronous, bool readMessage)
    {
        var suspension = new ManualSuspension();
        var payload = new object();
        state.Environment["payload"] = LuaValue.FromObject(payload);
        state.Environment["pause"] = new LuaFunction(
            async (context, _) =>
            {
                if (asynchronous)
                    await suspension;
                return context.Return();
            }
        );
        try
        {
            var operation = state
                .DoStringAsync("local value=payload;payload=nil;pause();error(value)")
                .AsTask();
            if (asynchronous)
                suspension.Resume();
            operation.GetAwaiter().GetResult();
            Assert.Fail("Expected a Lua error.");
        }
        catch (LuaRuntimeException exception)
        {
            if (readMessage)
                GC.KeepAlive(exception.Message);
        }
        return new WeakReference(payload);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DiscardedDirectCallErrorDoesNotRootPayload(bool asynchronous)
    {
        using var state = CreateState();
        AssertReleased(state, FailDirectCall(state, asynchronous));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference FailDirectCall(LuaState state, bool asynchronous)
    {
        var suspension = new ManualSuspension();
        var payload = new object();
        var function = new LuaFunction(
            async (context, _) =>
            {
                if (asynchronous)
                    await suspension;
                throw new LuaRuntimeException(context.State, context.GetArgument(0));
            }
        );
        try
        {
            var operation = state.CallAsync(function, [LuaValue.FromObject(payload)]).AsTask();
            if (asynchronous)
                suspension.Resume();
            operation.GetAwaiter().GetResult();
            Assert.Fail("Expected a Lua error.");
        }
        catch (LuaRuntimeException) { }
        return new WeakReference(payload);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DiscardedMetamethodErrorDoesNotRootCapturedPayload(bool asynchronous)
    {
        using var state = CreateState();
        AssertReleased(state, FailMetamethod(state, asynchronous));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference FailMetamethod(LuaState state, bool asynchronous)
    {
        var suspension = new ManualSuspension();
        var payload = new object();
        var table = new LuaTable
        {
            Metatable = new LuaTable
            {
                ["__add"] = new LuaFunction(
                    async (context, _) =>
                    {
                        if (asynchronous)
                            await suspension;
                        GC.KeepAlive(payload);
                        throw new LuaRuntimeException(context.State, "failure");
                    }
                ),
            },
        };
        try
        {
            var operation = state.AddAsync(table, 1).AsTask();
            if (asynchronous)
                suspension.Resume();
            operation.GetAwaiter().GetResult();
            Assert.Fail("Expected a Lua error.");
        }
        catch (LuaRuntimeException) { }
        return new WeakReference(payload);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DiscardedCancellationDoesNotRootInnerException(bool readTraceback)
    {
        using var state = CreateState();
        AssertReleased(state, CancelDirectCall(state, readTraceback));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference CancelDirectCall(LuaState state, bool readTraceback)
    {
        var payload = new object();
        var cancellation = new OperationCanceledException();
        cancellation.Data["payload"] = payload;
        var function = new LuaFunction((_, _) => throw cancellation);
        try
        {
            state.CallAsync(function, []).AsTask().GetAwaiter().GetResult();
            Assert.Fail("Expected cancellation.");
        }
        catch (LuaCanceledException exception)
        {
            if (readTraceback)
                GC.KeepAlive(exception.LuaTraceback);
        }
        return new WeakReference(payload);
    }

    [TestCase("call")]
    [TestCase("unary")]
    [TestCase("binary")]
    public void CancellationBeforeFirstFrameDoesNotRootExceptionOrTokenSource(string operation)
    {
        using var state = CreateState();
        var (exception, source) = CancelBeforeFirstFrame(state, operation);
        AssertReleased(state, exception);
        Assert.That(source.IsAlive, Is.False);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static (WeakReference Exception, WeakReference Source) CancelBeforeFirstFrame(
        LuaState state,
        string operation
    )
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var function = new LuaFunction((_, _) => throw new AssertionException("Unexpected call."));
        var table = new LuaTable
        {
            Metatable = new LuaTable { ["__add"] = function, ["__unm"] = function },
        };
        try
        {
            switch (operation)
            {
                case "call":
                    state.CallAsync(function, [], source.Token).AsTask().GetAwaiter().GetResult();
                    break;
                case "unary":
                    state.UnmAsync(table, source.Token).AsTask().GetAwaiter().GetResult();
                    break;
                case "binary":
                    state.AddAsync(table, table, source.Token).AsTask().GetAwaiter().GetResult();
                    break;
            }
        }
        catch (LuaCanceledException exception)
        {
            Assert.That(exception.CancellationToken, Is.EqualTo(source.Token));
            Assert.That(exception.LuaTraceback, Is.Null);
            return (new(exception), new(source));
        }
        throw new AssertionException("Expected cancellation.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ErrorsCreatedWithoutCallFramesDoNotRootPayloads(bool wrapped)
    {
        using var state = CreateState();
        AssertReleased(state, CreateIdleError(state, wrapped));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference CreateIdleError(LuaState state, bool wrapped)
    {
        var payload = new object();
        if (wrapped)
        {
            var inner = new InvalidOperationException("failure");
            inner.Data["payload"] = payload;
            GC.KeepAlive(new LuaRuntimeException(state, inner));
        }
        else
            GC.KeepAlive(new LuaRuntimeException(state, LuaValue.FromObject(payload)));
        return new(payload);
    }

    [Test]
    public async Task CancellationBeforeNestedCallPreservesCallerTraceback()
    {
        using var state = CreateState();
        using var source = new CancellationTokenSource();
        source.Cancel();
        state.Environment["cancel"] = new LuaFunction(
            async (context, _) =>
            {
                await context.State.CallAsync(
                    new LuaFunction((_, _) => throw new AssertionException("Unexpected call.")),
                    [],
                    source.Token
                );
                return context.Return();
            }
        );
        var exception = Assert.ThrowsAsync<LuaCanceledException>(async () =>
            await state.DoStringAsync("cancel()", "cancel-caller")
        )!;
        await state.DoStringAsync("return 42");
        Assert.That(exception.CancellationToken, Is.EqualTo(source.Token));
        Assert.That(exception.LuaTraceback!.StackFrames.Length, Is.EqualTo(2));
        Assert.That(exception.LuaTraceback.ToString(), Does.Contain("cancel-caller"));
    }

    [Test]
    public void EarlyTracebackReadDoesNotRootLaterUnwoundClosures()
    {
        using var state = CreateState();
        AssertReleased(state, ReadTracebackAndRethrow(state));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference ReadTracebackAndRethrow(LuaState state)
    {
        var payload = new object();
        state.Environment["payload"] = LuaValue.FromObject(payload);
        state.Environment["bounce"] = new LuaFunction(
            async (context, token) =>
            {
                try
                {
                    await context.State.DoStringAsync("error('failure')", "inner-chunk", token);
                }
                catch (LuaRuntimeException exception)
                {
                    GC.KeepAlive(exception.Message);
                    throw;
                }
                return context.Return();
            }
        );
        try
        {
            state
                .DoStringAsync(
                    "local p=payload;payload=nil;local function f() if p then bounce() end end;f()"
                )
                .AsTask()
                .GetAwaiter()
                .GetResult();
            Assert.Fail("Expected a Lua error.");
        }
        catch (LuaRuntimeException) { }
        return new WeakReference(payload);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RetainedErrorPreservesPayloadAndNestedTraceback(bool asynchronous)
    {
        using var state = CreateState();
        var payload = new object();
        state.Environment["payload"] = LuaValue.FromObject(payload);
        state.Environment["bounce"] = new LuaFunction(
            async (context, token) =>
            {
                if (asynchronous)
                    await Task.Yield();
                await context.State.DoStringAsync(
                    "local value=payload;payload=nil;error(value)",
                    "inner-chunk",
                    token
                );
                return context.Return();
            }
        );
        var exception = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.DoStringAsync("bounce()", "outer-chunk")
        )!;
        Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.DoStringAsync("error('another failure')", "other-chunk")
        );
        await state.DoStringAsync("return 42");

        Assert.That(exception.ErrorObject.Read<object>(), Is.SameAs(payload));
        Assert.That(exception.LuaTraceback!.StackFrames.Length, Is.EqualTo(4));
        var trace = exception.LuaTraceback.ToString();
        Assert.That(trace, Does.Contain("inner-chunk"));
        Assert.That(trace, Does.Contain("outer-chunk"));
        Assert.That(trace, Does.Not.Contain("other-chunk"));
    }

    static void AssertReleased(LuaState state, WeakReference reference)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.That(reference.IsAlive, Is.False);
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
        GC.KeepAlive(state);
    }
}
