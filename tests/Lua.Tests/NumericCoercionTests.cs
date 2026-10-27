using Lua.Standard;

namespace Lua.Tests;

public class NumericCoercionTests
{
    static LuaState CreateState()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        return state;
    }

    [TestCase("NaN")]
    [TestCase("nan")]
    [TestCase("Infinity")]
    [TestCase("-Infinity")]
    [TestCase("2\0")]
    [TestCase("2\0\0 ")]
    [TestCase("0x2\0")]
    [TestCase("\u00a02")]
    [TestCase("2\u00a0")]
    [TestCase("0x2\u00a0")]
    [TestCase("\u20032")]
    [TestCase("2\u2003")]
    public async Task NumericCoercionRejectsNonLuaSpellings(string text)
    {
        using var state = CreateState();
        state.Environment["text"] = text;
        await state.DoStringAsync(
            """
            assert(tonumber(text)==nil)
            local ok,e=pcall(function() return text+1 end)
            assert(not ok and e:find('string',1,true))
            ok,e=pcall(math.abs,text)
            assert(not ok and e:find('number expected',1,true))
            """
        );
        Assert.That(state.Stack.Count, Is.Zero);
    }

    [TestCase(" \f\n\r\t\v-2.5 \t", -2.5)]
    [TestCase("+.5", 0.5)]
    [TestCase("-0x1.8p2", -6)]
    [TestCase("1e309", double.PositiveInfinity)]
    public async Task LuaNumericWhitespaceSignsAndOverflowStillWork(string text, double expected)
    {
        using var state = CreateState();
        state.Environment["text"] = text;
        var values = await state.DoStringAsync("return tonumber(text),math.abs(text)");
        Assert.That(values[0].Read<double>(), Is.EqualTo(expected));
        Assert.That(values[1].Read<double>(), Is.EqualTo(Math.Abs(expected)));
    }
}
