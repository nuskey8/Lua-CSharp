using System.Globalization;
using Lua.Standard;

namespace Lua.Tests;

public class ManagedNumericTextTests
{
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
