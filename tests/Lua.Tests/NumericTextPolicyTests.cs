using System.Globalization;
using Lua.Standard;

namespace Lua.Tests;

public class NumericTextPolicyTests
{
    [TestCase("f")]
    [TestCase("e")]
    [TestCase("E")]
    [TestCase("g")]
    [TestCase("G")]
    [TestCase("a")]
    [TestCase("A")]
    public async Task ExplicitSpecialValuesUseCanonicalWords(string specifier)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["nan"] = double.NaN;
        var values = await state.DoStringAsync(
            $"return string.format('%{specifier}',math.huge),string.format('%{specifier}',-math.huge),string.format('%{specifier}',nan)"
        );
        var expected = new[] { "inf", "-inf", "-nan" };
        if (specifier is "E" or "G" or "A")
            expected = expected.Select(s => s.ToUpperInvariant()).ToArray();
        Assert.That(values.Select(v => v.Read<string>()), Is.EqualTo(expected));
    }

    [TestCase(1d / 3, "0.33333333333333")]
    [TestCase(1e20, "1e+20")]
    [TestCase(double.Epsilon, "4.9406564584125e-324")]
    [TestCase(double.PositiveInfinity, "inf")]
    [TestCase(double.NegativeInfinity, "-inf")]
    [TestCase(double.NaN, "-nan")]
    public async Task LuaFacingPathsUseTheProposedInvariantText(double number, string expected)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["number"] = number;
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var values = await state.DoStringAsync(
                """
                local x=setmetatable({}, {__tostring=function() return number end})
                local direct=string.gsub('x','x',number)
                local callback=string.gsub('x','x',function() return number end)
                local lookup=string.gsub('x','x',{x=number})
                return tostring(number),string.format('%s',number),number..'',
                    table.concat({number}),tostring(x),direct,callback,lookup
                """
            );
            Assert.That(values.Select(v => v.Read<string>()), Is.All.EqualTo(expected));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Test]
    public void HostToStringRetainsManagedPrecisionAndSpecialWords()
    {
        Assert.That(new LuaValue(1d / 3).ToString(), Is.EqualTo("0.3333333333333333"));
        Assert.That(new LuaValue(double.PositiveInfinity).ToString(), Is.EqualTo("Infinity"));
        Assert.That(new LuaValue(double.NaN).ToString(), Is.EqualTo("NaN"));
    }
}
