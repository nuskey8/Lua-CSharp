using Lua.Standard;

namespace Lua.Tests;

public class StackOverflowTests
{
    [Test]
    public void ExceedingMaxCallDepth_ThrowsLuaRuntimeException()
    {
        var state = LuaState.Create();
        state.MaxCallDepth = 5;

        var ex = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.DoStringAsync(
                """
                local function f()
                    f()
                end
                f()
                """
            ).AsTask()
        );

        Assert.That(ex!.InnerException, Is.TypeOf<LuaStackOverflowException>());
    }

    [Test]
    public async Task ExceedingMaxCallDepth_WithPCall_ReturnsErrorMessage()
    {
        var state = LuaState.Create();
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
        var state = LuaState.Create();
        state.MaxCallDepth = 100;

        var result = await state.DoStringAsync(
            """
            local function f(n)
                if n <= 0 then return 'done' end
                return f(n - 1)
            end
            return f(10)
            """
        );

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue("done")));
    }

    [Test]
    public async Task DefaultMaxCallDepth_AllowsDeepRecursion()
    {
        var state = LuaState.Create();

        var result = await state.DoStringAsync(
            """
            local function f(n)
                if n <= 0 then return n end
                return f(n - 1)
            end
            return f(1000)
            """
        );

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(0)));
    }
}
