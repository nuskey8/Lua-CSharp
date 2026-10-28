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
        var payload = new object();
        state.Environment["payload"] = LuaValue.FromObject(payload);
        state.Environment["pause"] = new LuaFunction(
            async (context, _) =>
            {
                if (asynchronous)
                    await Task.Yield();
                return context.Return();
            }
        );
        try
        {
            state
                .DoStringAsync("local value=payload;payload=nil;pause();error(value)")
                .AsTask()
                .GetAwaiter()
                .GetResult();
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
        var payload = new object();
        var function = new LuaFunction(
            async (context, _) =>
            {
                if (asynchronous)
                    await Task.Yield();
                throw new LuaRuntimeException(context.State, context.GetArgument(0));
            }
        );
        try
        {
            state
                .CallAsync(function, [LuaValue.FromObject(payload)])
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
    public void DiscardedMetamethodErrorDoesNotRootCapturedPayload(bool asynchronous)
    {
        using var state = CreateState();
        AssertReleased(state, FailMetamethod(state, asynchronous));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference FailMetamethod(LuaState state, bool asynchronous)
    {
        var payload = new object();
        var table = new LuaTable
        {
            Metatable = new LuaTable
            {
                ["__add"] = new LuaFunction(
                    async (context, _) =>
                    {
                        if (asynchronous)
                            await Task.Yield();
                        GC.KeepAlive(payload);
                        throw new LuaRuntimeException(context.State, "failure");
                    }
                ),
            },
        };
        try
        {
            state.AddAsync(table, 1).AsTask().GetAwaiter().GetResult();
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
