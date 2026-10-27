using Lua.Standard;

namespace Lua.Tests;

public class NumericLibraryRegressionTests
{
    static LuaState CreateState()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        return state;
    }

    [Test]
    public async Task NumberArgumentsKeepConversionAndErrors()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            assert(math.abs('-3') == 3)
            assert(math.sqrt('0x10') == 4)
            assert(math.max('1', '2', '3') == 3)
            assert(not pcall(math.abs))
            assert(not pcall(math.abs, nil))
            assert(not pcall(math.abs, false))
            local ok, message = pcall(math.max, 1, {})
            assert(not ok and string.find(message, '#2', 1, true))
            """
        );
    }

    [Test]
    public async Task MathKeepsLua52NanAndOptionalArgumentRules()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            local nan = 0/0
            assert(math.max(1, nan) == 1)
            assert(math.min(1, nan) == 1)
            local m = math.max(nan, 1)
            assert(m ~= m)
            m = math.min(nan, 1)
            assert(m ~= m)
            assert(1/math.max(-0.0, 0.0) == -math.huge)
            assert(1/math.min(0.0, -0.0) == math.huge)
            assert(math.log(2, nil) == math.log(2))
            """
        );
    }

    [Test]
    public async Task LogarithmKeepsLua52BaseEdgeCases()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            for _, base in ipairs({0.0, -0.0, '0'}) do
                local negativeZero = math.log(2, base)
                assert(negativeZero == 0 and 1/negativeZero == -math.huge)
                local positiveZero = math.log(0.5, base)
                assert(positiveZero == 0 and 1/positiveZero == math.huge)
                local one = math.log(1, base)
                assert(one == 0 and 1/one == -math.huge)
                local zero = math.log(0, base)
                assert(zero ~= zero)
                local negative = math.log(-1, base)
                assert(negative ~= negative)
                local infinite = math.log(math.huge, base)
                assert(infinite ~= infinite)
            end
            assert(math.log(2, 1) == math.huge)
            assert(math.log(0.5, 1) == -math.huge)
            local undefined = math.log(1, 1)
            assert(undefined ~= undefined)
            assert(math.log(8, 2) == 3)
            assert(math.log(100, 10) == 2)
            assert(math.log(2, nil) == math.log(2))
            assert(not pcall(math.log, 2, false))
            """
        );
    }
}
