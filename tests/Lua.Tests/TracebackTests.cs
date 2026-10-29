using Lua.Internal;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

public class TracebackTests
{
    [Test]
    public void ReversedStackGrowthPreservesExistingFramesAndOrder()
    {
        var stack = new ReversedStack<int>(4);
        stack.Push(1);
        stack.Push(2);
        stack.Push(new[] { 3, 4, 5 });
        Assert.That(stack.AsSpan().ToArray(), Is.EqualTo(new[] { 3, 4, 5, 2, 1 }));
    }

    [Test]
    public async Task FirstLineIsZeroWithoutLuaCaller()
    {
        using var state = LuaState.Create();
        Assert.That(state.GetTraceback().FirstLine, Is.Zero);
        await state.RunAsync(
            new LuaFunction(
                (context, _) => new(context.Return(context.State.GetTraceback().FirstLine))
            )
        );
        Assert.That(state.Stack.Pop(), Is.EqualTo(new LuaValue(0)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NestedFailurePreservesAllTracebackFrames(bool asynchronous)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["bounce"] = new LuaFunction(
            async (context, token) =>
            {
                if (asynchronous)
                    await Task.Yield();
                await context.State.DoStringAsync("error('failure')", "inner-chunk", token);
                return context.Return();
            }
        );
        var exception = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.DoStringAsync(
                "local function f(n) if n==0 then bounce() else f(n-1) end end; f(20)",
                "outer-chunk"
            )
        )!;
        var trace = exception.LuaTraceback!;
        var frames = trace.StackFrames.ToArray();
        Assert.That(frames.All(frame => frame.Function is not null), Is.True);
        Assert.That(trace.ToString(), Does.Contain("inner-chunk"));
        Assert.That(trace.ToString(), Does.Contain("outer-chunk"));
        state.Stack.Clear();
        await state.DoStringAsync("return 42");
        Assert.That(trace.StackFrames.ToArray(), Is.EqualTo(frames));
    }

    [Test]
    public async Task NestedExecutionRestoresTailCallerForArgumentErrors()
    {
        using var state = LuaState.Create();
        state.Environment["inner"] = new LuaFunction((context, _) => new(context.Return()));
        state.Environment["outer"] = new LuaFunction(
            async (context, token) =>
            {
                await context.State.DoStringAsync(
                    "local function f() return inner() end; return f()",
                    "inner-call",
                    token
                );
                LuaRuntimeException.BadArgument(context.State, 1, "bad value");
                return context.Return();
            }
        );
        var exception = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.DoStringAsync("return outer()", "outer-call")
        )!;
        Assert.That(exception.InnerException, Is.Null);
        Assert.That(exception.ErrorObject.Read<string>(), Does.Contain("to 'outer'"));
    }
}
