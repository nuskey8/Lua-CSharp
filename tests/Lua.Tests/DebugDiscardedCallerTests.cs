using Lua.Standard;

namespace Lua.Tests;

/// <summary>
/// A tail call into a host function replaces the caller's frame, so the caller exists only as a
/// recorded call site. Lua 5.2 keeps the frame and reports it from <c>debug.getinfo</c>; these tests
/// pin the same answers for the levels and fields that can be reconstructed, and the documented
/// limit: the discarded frame's registers are gone, so its locals cannot be read.
/// </summary>
public class DebugDiscardedCallerTests
{
    static LuaState CreateState()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        return state;
    }

    [Test]
    public async Task GetInfoReportsTheCallerDiscardedByANativeTailCall()
    {
        using var state = CreateState();
        var results = await state.DoStringAsync(
            "local function f(a)\n"
                + "  return debug.getinfo(1, 'Slu')\n"
                + "end\n"
                + "local info = f(1)\n"
                + "return info.what, info.linedefined, info.currentline, info.nparams, info.isvararg",
            "=discarded"
        );
        Assert.That(results[0].Read<string>(), Is.EqualTo("Lua"));
        Assert.That(results[1].Read<double>(), Is.EqualTo(1));
        Assert.That(results[2].Read<double>(), Is.EqualTo(2));
        Assert.That(results[3].Read<double>(), Is.EqualTo(1));
        Assert.That(results[4].Read<bool>(), Is.False);
    }

    [Test]
    public async Task GetInfoLevelTwoSkipsTheDiscardedCaller()
    {
        using var state = CreateState();
        var results = await state.DoStringAsync(
            "local function f()\n"
                + "  return debug.getinfo(2, 'S')\n"
                + "end\n"
                + "local info = f()\n"
                + "return info.what, info.linedefined",
            "=discarded"
        );
        Assert.That(results[0].Read<string>(), Is.EqualTo("main"));
        Assert.That(results[1].Read<double>(), Is.EqualTo(0));
    }

    [Test]
    public async Task GetLocalReadsTheFrameBelowTheDiscardedCaller()
    {
        using var state = CreateState();
        var results = await state.DoStringAsync(
            "local function f()\n"
                + "  return debug.getlocal(2, 1)\n"
                + "end\n"
                + "local name = f()\n"
                + "return name",
            "=discarded"
        );
        Assert.That(results[0].Read<string>(), Is.EqualTo("f"));
    }

    [Test]
    public async Task GetLocalCannotReadTheDiscardedCallersRegisters()
    {
        using var state = CreateState();
        var results = await state.DoStringAsync(
            "local function f(a)\n" + "  return debug.getlocal(1, 1)\n" + "end\n" + "return f(7)",
            "=discarded"
        );
        Assert.That(results[0].Type, Is.EqualTo(LuaValueType.Nil));
    }
}
