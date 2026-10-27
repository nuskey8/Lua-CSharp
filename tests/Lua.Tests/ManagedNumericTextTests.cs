using System.Globalization;
using Lua.Standard;

namespace Lua.Tests;

public class ManagedNumericTextTests
{
    [TestCase("f")]
    [TestCase("e")]
    [TestCase("E")]
    [TestCase("g")]
    [TestCase("G")]
    [TestCase("a")]
    [TestCase("A")]
    public async Task ExplicitSpecialValuesKeepManagedWords(string specifier)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["nan"] = double.NaN;
        var values = await state.DoStringAsync(
            $"return string.format('%{specifier}',math.huge),string.format('%{specifier}',-math.huge),string.format('%{specifier}',nan)"
        );
        var expected = new[] { "Infinity", "-Infinity", "NaN" };
        if (specifier is "E" or "G" or "A")
            expected = expected.Select(s => s.ToUpperInvariant()).ToArray();
        Assert.That(values.Select(v => v.Read<string>()), Is.EqualTo(expected));
    }

    [TestCase(1d / 3)]
    [TestCase(1e20)]
    [TestCase(double.Epsilon)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    [TestCase(double.NaN)]
    public async Task ImplicitTextKeepsInvariantManagedDefault(double number)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["number"] = number;
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var values = await state.DoStringAsync(
                "return tostring(number),string.format('%s',number),number..'',table.concat({number})"
            );
            Assert.That(
                values.Select(v => v.Read<string>()),
                Is.All.EqualTo(number.ToString(CultureInfo.InvariantCulture))
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
