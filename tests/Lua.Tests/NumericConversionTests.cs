using Lua.Standard;

namespace Lua.Tests;

public class NumericConversionTests
{
    static LuaState CreateState()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        return state;
    }

    [TestCase("101010", 2, 42d)]
    [TestCase("755", 8, 493d)]
    [TestCase("123456", 10, 123456d)]
    [TestCase("-ff", 16, -255d)]
    [TestCase("+aBc", 16, 2748d)]
    [TestCase(" \t\r\n-ff\v\f ", 16, -255d)]
    [TestCase("z1", 36, 1261d)]
    [TestCase("ffffffffffffffff", 36, 3.410854761405601E24)]
    [TestCase("18446744073709551616", 10, 18446744073709551616d)]
    public async Task ExplicitBaseAccumulatesDigitsLikeLua52(
        string text,
        int radix,
        double expected
    )
    {
        using var state = CreateState();
        var values = await state.CallAsync(state.Environment["tonumber"], [text, radix]);
        Assert.That(values[0].Read<double>(), Is.EqualTo(expected));
        Assert.That(state.Stack.Count, Is.Zero);
    }

    [TestCase("?", 2)]
    [TestCase("g", 16)]
    [TestCase("1.5", 10)]
    [TestCase("1.5", 16)]
    [TestCase("1e2", 10)]
    [TestCase("0x10", 16)]
    [TestCase("0x1p2", 16)]
    [TestCase("10 1", 2)]
    [TestCase("- 12", 10)]
    [TestCase("\u00a010\u00a0", 10)]
    [TestCase("10\0", 10)]
    [TestCase("", 10)]
    [TestCase("+", 16)]
    [TestCase("-", 36)]
    public async Task ExplicitBaseRejectsMalformedIntegers(string text, int radix)
    {
        using var state = CreateState();
        var values = await state.CallAsync(state.Environment["tonumber"], [text, radix]);
        Assert.That(values[0], Is.EqualTo(LuaValue.Nil));
    }

    [Test]
    public async Task NumberConversionKeepsNumericNaNAndSeparatesExplicitBaseParsing()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            local nan=0/0
            assert(tonumber(nan)~=tonumber(nan))
            assert(tonumber(math.huge)==math.huge)
            assert(tonumber(123,16)==291)
            assert(tonumber(123,2)==nil)
            assert(tonumber(1e14,10)==1e14) -- retains the managed numeric-text policy
            assert(tonumber('+12',10.9)==12)
            assert(tonumber('0x10',nil)==16)
            assert(tonumber(false)==nil)
            assert(not pcall(tonumber,{},10))
            assert(not pcall(tonumber,'12',1))
            assert(not pcall(tonumber,'12',37))
            assert(1/tonumber('-0',16)==-math.huge)
            """
        );
    }
}
