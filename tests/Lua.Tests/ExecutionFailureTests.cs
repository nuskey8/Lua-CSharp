using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

public class ExecutionFailureTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void FailedInvocationRestoresCallerPrefix(bool asynchronous, bool cancellation)
    {
        using var state = LuaState.Create();
        using var source = new CancellationTokenSource();
        var function = new LuaFunction(
            async (context, _) =>
            {
                context.State.Stack.Push("scratch");
                if (asynchronous)
                    await Task.Yield();
                if (cancellation)
                {
                    source.Cancel();
                    throw new OperationCanceledException(source.Token);
                }
                throw new InvalidOperationException("failure");
            }
        );
        state.Push(42);
        state.Push(99);
        if (cancellation)
            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await state.RunAsync(function, 1, source.Token)
            );
        else
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await state.RunAsync(function, 1)
            );
        Assert.That(state.CallStackFrameCount, Is.Zero);
        Assert.That(state.Stack.Count, Is.EqualTo(1));
        Assert.That(state.Pop(), Is.EqualTo(new LuaValue(42)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NestedFailurePreservesManagedCallbackArguments(bool asynchronous)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["fail"] = new LuaFunction(
            async (context, _) =>
            {
                if (asynchronous)
                    await Task.Yield();
                throw new LuaRuntimeException(context.State, "failure");
            }
        );
        state.Environment["callback"] = new LuaFunction(
            async (context, token) =>
            {
                try
                {
                    await context.State.DoStringAsync(
                        "local a,b,c=1,2,3; fail()",
                        "nested-failure",
                        token
                    );
                }
                catch (LuaRuntimeException) { }
                return context.Return(context.GetArgument(0));
            }
        );
        var result = await state.DoStringAsync("return callback(42)");
        Assert.That(result, Is.EqualTo(new LuaValue[] { 42 }));
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [Test]
    public async Task FrameSetupFailureRestoresCallerPrefix()
    {
        using var state = LuaState.Create();
        var function = (await state.DoStringAsync("return function(a,b,...) return a,b end"))[0]
            .Read<LuaFunction>();
        var caller = new LuaFunction((context, _) => new(context.Return()));
        state.MaxCallDepth = 1;
        state.Push(42);
        state.PushCallStackFrame(
            new()
            {
                Base = 0,
                ReturnBase = 0,
                Function = caller,
                VariableArgumentCount = 0,
            }
        );
        try
        {
            Assert.ThrowsAsync<LuaStackOverflowException>(async () =>
                await state.RunAsync(function)
            );
            Assert.That(state.CallStackFrameCount, Is.EqualTo(1));
            Assert.That(state.Stack.Count, Is.EqualTo(1));
            Assert.That(state.Pop(), Is.EqualTo(new LuaValue(42)));
        }
        finally
        {
            state.PopCallStackFrame();
        }
    }

    [Test]
    public void AlreadyCancelledInvocationRestoresCallerPrefix()
    {
        using var state = LuaState.Create();
        using var source = new CancellationTokenSource();
        source.Cancel();
        var function = new LuaFunction((context, _) => new(context.Return()));
        state.Push(42);
        state.Push(99);
        Assert.ThrowsAsync<LuaCanceledException>(async () =>
            await state.RunAsync(function, 1, source.Token)
        );
        Assert.That(state.CallStackFrameCount, Is.Zero);
        Assert.That(state.Stack.Count, Is.EqualTo(1));
        Assert.That(state.Pop(), Is.EqualTo(new LuaValue(42)));
    }
}
