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
    public async Task ErrorFromTailCalledLibraryFunctionReportsTheCallingLine()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var results = await state.DoStringAsync(
            "local function call()\n"
                + "  return string.rep(true)\n"
                + "end\n"
                + "local ok, err = pcall(call)\n"
                + "return ok, err",
            "=tailcall"
        );
        Assert.That(results[0].Read<bool>(), Is.False);
        Assert.That(results[1].ToString(), Does.Contain(":2: bad argument #1 to 'rep'"));
    }

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
