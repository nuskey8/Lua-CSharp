using Lua.Internal;

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
        Assert.That(Math.Abs(HexConverter.ToDouble(text) - expected), Is.LessThanOrEqualTo(0.00001d));
    }
}