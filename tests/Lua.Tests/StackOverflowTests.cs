using System.Runtime.CompilerServices;
using Lua.Standard;

namespace Lua.Tests;

public class StackOverflowTests
{
    [Test]
    public void ExceedingMaxCallDepth_ThrowsLuaRuntimeException()
    {
        using var state = LuaState.Create();
        state.MaxCallDepth = 5;

        var ex = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state
                .DoStringAsync(
                    """
                    local function f()
                        f()
                    end
                    f()
                    """
                )
                .AsTask()
        );

        Assert.That(ex!.InnerException, Is.TypeOf<LuaStackOverflowException>());
    }

    [Test]
    public async Task ExceedingMaxCallDepth_WithPCall_ReturnsErrorMessage()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.MaxCallDepth = 5;

        var result = await state.DoStringAsync(
            """
            local function f()
                f()
            end
            local ok, msg = pcall(f)
            return ok, msg
            """
        );

        Assert.That(result, Has.Length.EqualTo(2));
        Assert.That(result[0], Is.EqualTo(new LuaValue(false)));
        Assert.That(result[1].Read<string>(), Does.Contain("stack overflow"));
    }

    [Test]
    public async Task UnderMaxCallLimit_Succeeds()
    {
        using var state = LuaState.Create();
        state.MaxCallDepth = 100;

        var result = await state.DoStringAsync(
            """
            local function f(n)
                if n <= 0 then return 0 end
                return 1 + f(n - 1)
            end
            return f(10)
            """
        );

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(10)));
    }

    [Test]
    public async Task DefaultMaxCallDepth_AllowsDeepRecursion()
    {
        using var state = LuaState.Create();

        var result = await state.DoStringAsync(
            """
            local function f(n)
                if n <= 0 then return n end
                return 1 + f(n - 1)
            end
            return f(1000)
            """
        );

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(1000)));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void InvalidMaxCallDepth_IsRejected(int limit)
    {
        using var state = LuaState.Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => state.MaxCallDepth = limit);
        Assert.That(state.MaxCallDepth, Is.EqualTo(100_000));
    }

    [Test]
    public void MaxCallDepth_IsSharedWithExistingThreads()
    {
        using var state = LuaState.Create();
        using var child = state.CreateThread();
        state.MaxCallDepth = 5;
        Assert.That(child.MaxCallDepth, Is.EqualTo(5));
        child.MaxCallDepth = 10;
        Assert.That(state.MaxCallDepth, Is.EqualTo(10));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CoroutineCannotBypassMaxCallDepth(bool wrap)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.MaxCallDepth = 5;
        var result = await state.DoStringAsync(
            """
            local function body()
                local function f(n)
                    if n == 0 then return 0 end
                    return 1 + f(n - 1)
                end
                return f(20)
            end
            """
                + "\n"
                + (
                    wrap
                        ? "return pcall(coroutine.wrap(body))"
                        : "return coroutine.resume(coroutine.create(body))"
                )
        );
        Assert.That(result[0].ToBoolean(), Is.False);
        Assert.That(result[1].Read<string>(), Does.Contain("stack overflow"));
        Assert.That(state.CallStackFrameCount, Is.Zero);
        Assert.That((await state.DoStringAsync("return 42"))[0].Read<int>(), Is.EqualTo(42));
    }

    [Test]
    public async Task CoroutineResume_PreservesLuaNilErrors()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var result = await state.DoStringAsync(
            "return coroutine.resume(coroutine.create(function() error(nil) end))"
        );
        Assert.That(result, Has.Length.EqualTo(2));
        Assert.That(result[0].ToBoolean(), Is.False);
        Assert.That(result[1], Is.EqualTo(LuaValue.Nil));
    }

    [Test]
    public async Task TailCallsReuseDepthAtTheLimit()
    {
        using var state = LuaState.Create();
        state.MaxCallDepth = 1;
        var result = await state.DoStringAsync(
            "local function f(n) if n == 0 then return 42 end return f(n - 1) end return f(10000)"
        );
        Assert.That(result[0].Read<int>(), Is.EqualTo(42));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NativeReentryIsGuarded(bool throughLua)
    {
        Exception? caught = null;
        var enteredWithInsufficientStack = false;
        var calls = 0;
        var worker = new Thread(
            () =>
            {
                using var state = LuaState.Create();
                LuaFunction? callback = null;
                var bridge = state.Load("return bounce()", "bridge.lua");
                callback = new LuaFunction(
                    "bounce",
                    (context, ct) =>
                    {
                        calls++;
                        // Stop before an actual overflow if a guard is missing, so a regression
                        // fails this test instead of terminating the test process.
                        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
                        {
                            enteredWithInsufficientStack = true;
                            return new(context.Return());
                        }
                        if (calls >= 10000)
                            return new(context.Return());
                        return context.State.RunAsync(throughLua ? bridge : callback!, ct);
                    }
                );
                state.Environment["bounce"] = callback;
                try
                {
                    state.RunAsync(callback).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
            },
            1024 * 1024
        );
        worker.Start();
        worker.Join();
        Assert.That(enteredWithInsufficientStack, Is.False);
        Assert.That(
            caught is LuaStackOverflowException
                || caught?.InnerException is LuaStackOverflowException,
            Is.True
        );
        Assert.That(calls, Is.LessThan(10000));
    }
}
