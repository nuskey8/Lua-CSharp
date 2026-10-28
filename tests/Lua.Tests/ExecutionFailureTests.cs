using System.Runtime.CompilerServices;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

public class ExecutionFailureTests
{
    [Test]
    public async Task CallApiPreservesArgumentsBorrowedFromStackBuffer()
    {
        using var state = LuaState.Create();
        var function = new LuaFunction(
            (context, _) =>
                new(context.Return(context.GetArgument<double>(0) + context.GetArgument<double>(1)))
        );
        state.Stack.GetBuffer()[0] = 42;
        state.Stack.GetBuffer()[1] = 43;
        var values = await state.CallAsync(function, state.Stack.GetBuffer()[..2]);
        Assert.That(values, Is.EqualTo(new LuaValue[] { 85 }));
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NestedCallApiFailurePreservesManagedCallbackArguments(bool asynchronous)
    {
        using var state = LuaState.Create();
        var failure = new LuaFunction(
            async (_, _) =>
            {
                if (asynchronous)
                    await Task.Yield();
                throw new InvalidOperationException("failure");
            }
        );
        state.Environment["callback"] = new LuaFunction(
            async (context, token) =>
            {
                try
                {
                    await context.State.CallAsync(failure, [13, 14], token);
                }
                catch (InvalidOperationException) { }
                return context.Return(context.GetArgument(0));
            }
        );
        var values = await state.DoStringAsync("return callback(42)");
        Assert.That(values, Is.EqualTo(new LuaValue[] { 42 }));
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void FailedCallApiRestoresCallerPrefix(bool asynchronous, bool cancellation)
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
        if (cancellation)
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await state.CallAsync(function, [99], source.Token)
            );
        else
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await state.CallAsync(function, [99])
            );
        Assert.That(state.CallStackFrameCount, Is.Zero);
        Assert.That(state.Stack.Count, Is.EqualTo(1));
        Assert.That(state.Pop(), Is.EqualTo(new LuaValue(42)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FailedCallApiDoesNotRootArguments(bool asynchronous)
    {
        using var state = LuaState.Create();
        var reference = FailCallApi(state, asynchronous);
        state.DoStringAsync("return 42").AsTask().GetAwaiter().GetResult();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.That(reference.IsAlive, Is.False);
        Assert.That(state.Stack.Count, Is.Zero);
        GC.KeepAlive(state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference FailCallApi(LuaState state, bool asynchronous)
    {
        var payload = new object();
        var function = new LuaFunction(
            async (_, _) =>
            {
                if (asynchronous)
                    await Task.Yield();
                throw new InvalidOperationException("failure");
            }
        );
        try
        {
            state
                .CallAsync(function, [LuaValue.FromObject(payload)])
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        catch (InvalidOperationException) { }
        return new WeakReference(payload);
    }

    [Test]
    public void NonCallableCallApiRestoresCallerPrefix()
    {
        using var state = LuaState.Create();
        state.Push(42);
        Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.CallAsync(LuaValue.Nil, [99])
        );
        Assert.That(state.Stack.Count, Is.EqualTo(1));
        Assert.That(state.Pop(), Is.EqualTo(new LuaValue(42)));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

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

    [TestCase(false)]
    [TestCase(true)]
    public async Task FrameSetupFailureRestoresCallerPrefix(bool callApi)
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
            {
                if (callApi)
                    await state.CallAsync(function, []);
                else
                    await state.RunAsync(function);
            });
            Assert.That(state.CallStackFrameCount, Is.EqualTo(1));
            Assert.That(state.Stack.Count, Is.EqualTo(1));
            Assert.That(state.Pop(), Is.EqualTo(new LuaValue(42)));
        }
        finally
        {
            state.PopCallStackFrame();
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AlreadyCancelledInvocationRestoresCallerPrefix(bool callApi)
    {
        using var state = LuaState.Create();
        using var source = new CancellationTokenSource();
        source.Cancel();
        var function = new LuaFunction((context, _) => new(context.Return()));
        state.Push(42);
        Assert.ThrowsAsync<LuaCanceledException>(async () =>
        {
            if (callApi)
                await state.CallAsync(function, [99], source.Token);
            else
            {
                state.Push(99);
                await state.RunAsync(function, 1, source.Token);
            }
        });
        Assert.That(state.CallStackFrameCount, Is.Zero);
        Assert.That(state.Stack.Count, Is.EqualTo(1));
        Assert.That(state.Pop(), Is.EqualTo(new LuaValue(42)));
    }
}
