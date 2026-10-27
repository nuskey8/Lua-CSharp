using System.Globalization;
using Lua.Standard;

namespace Lua.Tests;

public class FormatCompatibilityTests
{
    [TestCase("%d", "1.9", "1")]
    [TestCase("%i", "-1.9", "-1")]
    [TestCase("%u", "-0.5", "0")]
    [TestCase("%x", "15.9", "f")]
    [TestCase("%X", "15.9", "F")]
    [TestCase("%c", "65.9", "A")]
    [TestCase("%+08d", "-12.75", "-0000012")]
    [TestCase("% 08d", "12.75", " 0000012")]
    [TestCase("%+ 8d", "12.75", "     +12")]
    [TestCase("%+8u", "12.75", "      12")]
    [TestCase("%#08x", "15.9", "0x00000f")]
    [TestCase("%#08X", "15.9", "0X00000F")]
    [TestCase("%#8x", "0", "       0")]
    [TestCase("%08.3x", "15.9", "     00f")]
    [TestCase("%.0d", "0", "")]
    [TestCase("%.0u", "0", "")]
    [TestCase("%.0x", "0", "")]
    [TestCase("%f", "1.5", "1.500000")]
    [TestCase("%e", "1.5", "1.500000e+00")]
    [TestCase("%E", "1.5", "1.500000E+00")]
    [TestCase("%g", "0.000012345", "1.2345e-05")]
    [TestCase("%.0g", "12.75", "1e+01")]
    [TestCase("%#.3g", "1.5", "1.50")]
    [TestCase("%#.3g", "0.0015", "0.00150")]
    [TestCase("%#.3g", "0", "0.00")]
    [TestCase("%#.0f", "1.5", "2.")]
    [TestCase("%#.0e", "1.5", "2.e+00")]
    [TestCase("%f", "-0.0", "-0.000000")]
    [TestCase("%++d", "2", "+2")]
    [TestCase("%+++++d", "2", "+2")]
    [TestCase("%08s", "'0x'", "0000000x")]
    [TestCase("%08s", "'-x'", "000000-x")]
    [TestCase("%08c", "45", "0000000-")]
    [TestCase("%20.0q", "'text'", "\"text\"")]
    [TestCase("%d", "-2^63", "-9223372036854775808")]
    [TestCase("%u", "2^64-2048", "18446744073709549568")]
    public async Task FormatsFollowLua52(string format, string expression, string expected)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.NegativeSign = "minus";
            CultureInfo.CurrentCulture = culture;
            var values = await state.DoStringAsync(
                $"return string.format('{format}',{expression})"
            );
            Assert.That(values[0].Read<string>(), Is.EqualTo(expected));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [TestCase("%d", "2^63", "2")]
    [TestCase("%u", "2^64", "2")]
    [TestCase("%x", "-1", "2")]
    [TestCase("%f", "false", "2")]
    [TestCase("%d", "false", "2")]
    [TestCase("%s:%d", "'prefix',false", "3")]
    public async Task FormatErrorsUseLuaExceptionsAndCorrectArgumentNumbers(
        string format,
        string arguments,
        string number
    )
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var values = await state.DoStringAsync(
            $"return pcall(string.format,'{format}',{arguments})"
        );
        Assert.That(values[0].Read<bool>(), Is.False);
        Assert.That(values[1].Read<string>(), Does.Contain("bad argument #" + number));
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [Test]
    public async Task SixFlagsStillReject()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var values = await state.DoStringAsync("return pcall(string.format,'%++++++d',1)");
        Assert.That(values[0].Read<bool>(), Is.False);
        Assert.That(values[1].Read<string>(), Does.Contain("repeated flags"));
    }
}
