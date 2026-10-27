using System.Globalization;
using Lua.Standard;

namespace Lua.Tests;

public class StringFormatTests
{
    LuaState CreateState()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        return state;
    }

    [TestCase("true", "boolean")]
    [TestCase("false", "boolean")]
    [TestCase("nil", "nil")]
    [TestCase("{}", "table")]
    [TestCase("function() end", "function")]
    [TestCase("coroutine.create(function() end)", "thread")]
    public async Task QuotedFormatRejectsUnsupportedTypes(string expression, string type)
    {
        using var state = CreateState();
        foreach (
            var (format, arguments, argumentNumber) in new[]
            {
                ("%q", expression, 2),
                ("%s:%q", "'prefix'," + expression, 3),
            }
        )
        {
            var values = await state.DoStringAsync(
                $"return pcall(string.format,'{format}',{arguments})"
            );
            Assert.That(values[0].Read<bool>(), Is.False);
            Assert.That(values[1].Read<string>(), Does.Contain($"bad argument #{argumentNumber}"));
            Assert.That(values[1].Read<string>(), Does.Contain("string expected"));
            Assert.That(values[1].Read<string>(), Does.Contain("got " + type));
            Assert.That(state.Stack.Count, Is.Zero);
            Assert.That(state.CallStackFrameCount, Is.Zero);
        }
    }

    [Test]
    public async Task QuotedFormatDoesNotInvokeToStringMetamethod()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            local calls=0
            local value=setmetatable({}, {__tostring=function() calls=calls+1; return 'converted' end})
            local ok,e=pcall(string.format,'%q',value)
            assert(not ok and e:find('string expected',1,true) and calls==0)
            assert(string.format('%s',value)=='converted' and calls==1)
            assert(string.format('%s,%s,%s',true,false,nil)=='true,false,nil')
            """
        );
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase("%a", "0", "0x0p+0")]
    [TestCase("%a", "-0.0", "-0x0p+0")]
    [TestCase("%a", "0x1p-1074", "0x0.0000000000001p-1022")]
    [TestCase("%A", "0x1fffffffffffff", "0X1.FFFFFFFFFFFFFP+52")]
    [TestCase("%.2a", "0.5", "0x1.00p-1")]
    [TestCase("%.4a", "-3", "-0x1.8000p+1")]
    [TestCase("%.0a", "0x1.8p0", "0x2p+0")]
    [TestCase("%#.0a", "0x1.ffp0", "0x2.p+0")]
    [TestCase("%.1a", "0x1.28p0", "0x1.2p+0")]
    [TestCase("%.1a", "0x1.38p0", "0x1.4p+0")]
    [TestCase("%.1a", "0x1.ffp0", "0x2.0p+0")]
    [TestCase("%.1a", "0x0.fffffffffffffp-1022", "0x1.0p-1022")]
    [TestCase("%+020.2a", "1.5", "+0x00000000001.80p+0")]
    [TestCase("% 20.1A", "0x1.ffp0", "            0X2.0P+0")]
    [TestCase("%-20.1a", "0x1.ffp0", "0x2.0p+0            ")]
    [TestCase("%.14a", "1.5", "0x1.80000000000000p+0")]
    [TestCase("%#.a", "1", "0x1.p+0")]
    [TestCase("%a", "math.huge", "Infinity")]
    [TestCase("%+A", "math.huge", "+INFINITY")]
    [TestCase("%010a", "-math.huge", " -Infinity")]
    [TestCase("%o", "8", "10")]
    [TestCase("%o", "1.5", "1")]
    [TestCase("%o", "-0.5", "0")]
    [TestCase("%o", "2^63", "1000000000000000000000")]
    [TestCase("%o", "2^64-2048", "1777777777777777774000")]
    [TestCase("%#.0o", "0", "0")]
    [TestCase("%.0o", "0", "")]
    [TestCase("%#08o", "8", "00000010")]
    [TestCase("%-8.5o", "8", "00010   ")]
    [TestCase("%08.5o", "8", "   00010")]
    [TestCase("%+o", "8", "10")]
    [TestCase("% o", "8", "10")]
    [TestCase("%.o", "0", "")]
    [TestCase("%.0s", "'text'", "")]
    public async Task FormatSpecifiersAndPrecisionFollowLua52(
        string format,
        string value,
        string expected
    )
    {
        using var state = CreateState();
        var values = await state.DoStringAsync($"return string.format('{format}',{value})");
        Assert.That(values[0].Read<string>(), Is.EqualTo(expected));
    }

    [TestCase("%")]
    [TestCase("%1")]
    [TestCase("%#")]
    [TestCase("%.")]
    [TestCase("%10.")]
    [TestCase("%100o")]
    [TestCase("%.100a")]
    public async Task InvalidFormatsRaiseLuaErrors(string format)
    {
        using var state = CreateState();
        var values = await state.DoStringAsync(
            $"local ok,e=pcall(string.format,'{format}',1); return ok,e"
        );
        Assert.That(values[0].Read<bool>(), Is.False);
        Assert.That(values[1].Read<string>(), Does.Contain("format"));
    }

    [TestCase("-1")]
    [TestCase("2^64")]
    [TestCase("math.huge")]
    [TestCase("0/0")]
    [TestCase("{}")]
    public async Task OctalRejectsValuesOutsideUnsignedRange(string value)
    {
        using var state = CreateState();
        var values = await state.DoStringAsync($"return pcall(string.format,'%o',{value})");
        Assert.That(values[0].Read<bool>(), Is.False);
        Assert.That(values[1].Read<string>(), Does.Contain("bad argument #2"));
    }

    [Test]
    public async Task HexadecimalFormatRoundTripsFiniteNumbersUnderOtherCultures()
    {
        using var state = CreateState();
        var format = state.Environment["string"].Read<LuaTable>()["format"];
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            var culture = (CultureInfo)CultureInfo.GetCultureInfo("fr-FR").Clone();
            culture.NumberFormat.NegativeSign = "~";
            culture.NumberFormat.PositiveSign = "!";
            CultureInfo.CurrentCulture = culture;
            var random = new Random(42);
            for (var i = 0; i < 512; i++)
            {
                var bits = random.NextInt64(long.MinValue, long.MaxValue);
                if ((i & 1) == 0)
                    bits &= unchecked((long)0x800fffffffffffff);
                var number = BitConverter.Int64BitsToDouble(bits);
                if (!double.IsFinite(number))
                    continue;
                var values = await state.CallAsync(format, [i % 2 == 0 ? "%a" : "%A", number]);
                var text = values[0].Read<string>();
                Assert.That(
                    BitConverter.DoubleToInt64Bits(Internal.HexConverter.ToDouble(text)),
                    Is.EqualTo(bits),
                    text
                );
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
