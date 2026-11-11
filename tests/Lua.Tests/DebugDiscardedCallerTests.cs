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

    [TestCase(false)]
    [TestCase(true)]
    public async Task GetInfoReportsTheCallerDiscardedByANativeTailCall(bool tailCall)
    {
        using var state = CreateState();
        var invoke = tailCall
            ? "local function outer() return f(1) end\nlocal info = outer()"
            : "local info = f(1)";
        var results = await state.DoStringAsync(
            $"""
            local function f(a)
              return debug.getinfo(1, 'SluLntf')
            end
            {invoke}
            return info.what, info.linedefined, info.currentline, info.nparams, info.isvararg,
                   info.istailcall, info.name, info.activelines[2], info.func
            """,
            "=discarded"
        );
        Assert.That(
            results,
            Is.EqualTo(
                new LuaValue[]
                {
                    "Lua",
                    1,
                    2,
                    1,
                    false,
                    tailCall,
                    tailCall ? LuaValue.Nil : "f",
                    true,
                    LuaValue.Nil,
                }
            )
        );
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
