using System.Globalization;
using Lua.Standard;

namespace Lua.Tests;

public class NumericQuotingTests
{
    [TestCase(12d)]
    [TestCase(1d / 3)]
    [TestCase(-0d)]
    [TestCase(double.Epsilon)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NaN)]
    public async Task NumericQuoteReturnsAQuotedStringUsingManagedText(double number)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["number"] = number;
        foreach (var format in new[] { "%q", "%20q", "%+.2q" })
        {
            var values = await state.DoStringAsync(
                $"local q=string.format('{format}',number); return q,assert(load('return '..q))()"
            );
            var text = number.ToString(CultureInfo.InvariantCulture);
            Assert.That(values[0].Read<string>(), Is.EqualTo('"' + text + '"'));
            Assert.That(values[1].Type, Is.EqualTo(LuaValueType.String));
            Assert.That(values[1].Read<string>(), Is.EqualTo(text));
        }
    }
}
