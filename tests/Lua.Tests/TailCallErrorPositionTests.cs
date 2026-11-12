using Lua.Standard;

namespace Lua.Tests;

/// <summary>
/// Lua 5.2 tail-calls only Lua closures, so an error raised by a library function reached through
/// <c>return f()</c> still reports the calling line. This engine discards the caller's frame to let
/// its values be collected, but records the call site; errors and tracebacks must report it.
/// </summary>
public class TailCallErrorPositionTests
{
    [Test]
    public async Task NestedTailCallsReportTheInnermostCallingLine()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var results = await state.DoStringAsync(
            "local function inner()\n"
                + "  return table.concat(1)\n"
                + "end\n"
                + "local function outer()\n"
                + "  return inner()\n"
                + "end\n"
                + "local ok, err = pcall(outer)\n"
                + "return ok, err",
            "=nested"
        );
        Assert.That(results[0].Read<bool>(), Is.False);
        Assert.That(results[1].ToString(), Does.Contain(":2: bad argument #1 to 'concat'"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TailCallSiteTakesPrecedenceOverOlderLuaFrames(bool protectedCall)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var closure = state.Load(
            """
            local function inner()
              return string.rep(true)
            end
            local function middle()
              return (inner())
            end
            return (middle())
            """,
            "=nested"
        );

        string message;
        if (protectedCall)
        {
            state.Environment["call"] = closure;
            var results = await state.DoStringAsync("return pcall(call)");
            Assert.That(results[0].Read<bool>(), Is.False);
            message = results[1].Read<string>();
        }
        else
        {
            var exception = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
                await state.ExecuteAsync(closure)
            );
            message = exception!.Message;
        }
        Assert.That(message, Does.Contain("nested:2: bad argument #1 to 'rep'"));
    }

    [Test]
    public void TailCalledErrorAtLevelZeroOmitsTheSourcePosition()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var exception = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.DoStringAsync("return error('message', 0)", "=level-zero")
        );
        Assert.That(exception!.Message, Is.EqualTo("Lua-CSharp: message"));
    }

    [Test]
    public void TailCallSiteSurvivesChangesToLineHookState()
    {
        using var state = LuaState.Create();
        var hook = new LuaFunction((context, _) => new(context.Return()));
        state.SetHook(hook, "c");
        state.Environment["fail"] = new LuaFunction(
            (context, _) =>
            {
                context.State.SetHook(hook, "l");
                throw new LuaRuntimeException(context.State, "message");
            }
        );

        var exception = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.DoStringAsync("local value = 42\nreturn fail()", "=tail-hook")
        );
        Assert.That(exception!.Message, Is.EqualTo("Lua-CSharp: tail-hook:2: message"));
    }

    [Test]
    public async Task OrdinaryCallsKeepReportingTheirLine()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var results = await state.DoStringAsync(
            "local function call()\n"
                + "  local result = string.rep(true)\n"
                + "  return result\n"
                + "end\n"
                + "local ok, err = pcall(call)\n"
                + "return ok, err",
            "=ordinary"
        );
        Assert.That(results[0].Read<bool>(), Is.False);
        Assert.That(results[1].ToString(), Does.Contain(":2: bad argument #1 to 'rep'"));
    }
}
