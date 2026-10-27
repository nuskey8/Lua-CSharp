using Lua.Internal;
using Lua.Standard;

namespace Lua.Tests;

public class HexConverterTests
{
    [TestCase("0x10", 16)]
    [TestCase("0x0p12", 0)]
    [TestCase("-0x1.0p-1", -0.5)]
    [TestCase("0x0.1e", 0.1171875)]
    [TestCase("0xA23p-4", 162.1875)]
    [TestCase("0X1.921FB54442D18P+1", 3.1415926535898)]
    [TestCase("0X1.bcde19p+1", 3.475527882576)]
    public void Test_ToDouble(string text, double expected)
    {
        Assert.That(
            Math.Abs(HexConverter.ToDouble(text) - expected),
            Is.LessThanOrEqualTo(0.00001d)
        );
    }

    [TestCase(1.1, "0x1.199999999999ap+0")]
    [TestCase(double.PositiveInfinity, "1e9999")]
    [TestCase(double.NegativeInfinity, "-1e9999")]
    [TestCase(double.NaN, "(0/0)")]
    [TestCase(-1.5, "-0x1.8p+0")]
    [TestCase(1.2e-39, "0x1.a22393b33036bp-130")]
    [TestCase(double.Epsilon, "0x0.0000000000001p-1022")]
    [TestCase(-double.Epsilon, "-0x0.0000000000001p-1022")]
    [TestCase(1.668805393880401E-308, "0x0.cp-1022")]
    [TestCase(2.225073858507201E-308, "0x0.fffffffffffffp-1022")]
    [TestCase(2.2250738585072014E-308, "0x1p-1022")]
    public void Test_FromDouble(double value, string expected)
    {
        Assert.That(HexConverter.FromDouble(value), Is.EqualTo(expected.ToLower()));
    }

    [Test]
    public void FiniteHexFormattingRoundTripsBitExactly()
    {
        var random = new Random(42);
        for (var i = 0; i < 2048; i++)
        {
            var bits = random.NextInt64(long.MinValue, long.MaxValue);
            if ((i & 1) == 0)
                bits &= unchecked((long)0x800fffffffffffff); // Include subnormals.
            var value = BitConverter.Int64BitsToDouble(bits);
            if (!double.IsFinite(value))
                continue;
            var text = HexConverter.FromDouble(value);
            Assert.That(
                BitConverter.DoubleToInt64Bits(HexConverter.ToDouble(text)),
                Is.EqualTo(bits),
                text
            );
        }
    }
}
