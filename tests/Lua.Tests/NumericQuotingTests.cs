using Lua.Standard;

namespace Lua.Tests;

public class NumericQuotingTests
{
    [TestCase(12d, "12")]
    [TestCase(1d / 3, "0.33333333333333")]
    [TestCase(-0d, "-0")]
    [TestCase(double.Epsilon, "4.9406564584125e-324")]
    [TestCase(double.PositiveInfinity, "inf")]
    [TestCase(double.NaN, "-nan")]
    public async Task NumericQuoteReturnsAQuotedStringUsingProposedText(double number, string text)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["number"] = number;
        foreach (var format in new[] { "%q", "%20q", "%+.2q" })
        {
            var values = await state.DoStringAsync(
                $"local q=string.format('{format}',number); return q,assert(load('return '..q))()"
            );
            Assert.That(values[0].Read<string>(), Is.EqualTo('"' + text + '"'));
            Assert.That(values[1].Type, Is.EqualTo(LuaValueType.String));
            Assert.That(values[1].Read<string>(), Is.EqualTo(text));
        }
    }
}
