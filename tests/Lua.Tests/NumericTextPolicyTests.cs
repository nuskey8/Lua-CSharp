using System.Globalization;
using System.Text;
using Lua.IO;
using Lua.Platforms;
using Lua.Standard;
using Lua.Tests.Helpers;

namespace Lua.Tests;

[TestFixture(LuaNumberFormat.DotNet)]
[TestFixture(LuaNumberFormat.Lua)]
public class NumericTextPolicyTests(LuaNumberFormat numberFormat)
{
    LuaState CreateState(LuaStringMode stringMode = LuaStringMode.Utf16)
    {
        var platform =
            stringMode == LuaStringMode.Bytes
                ? LuaPlatform.DefaultByteStrings
                : LuaPlatform.Default;
        var state = LuaState.Create(platform with { NumberFormat = numberFormat });
        state.OpenStandardLibraries();
        return state;
    }

    string Expected(double value, string luaText) =>
        numberFormat == LuaNumberFormat.Lua
            ? luaText
            : value.ToString(CultureInfo.InvariantCulture);

    [TestCase(0d, "0")]
    [TestCase(-0d, "-0")]
    [TestCase(1d / 3, "0.33333333333333")]
    [TestCase(1e-4, "0.0001")]
    [TestCase(1e-5, "1e-05")]
    [TestCase(1e13, "10000000000000")]
    [TestCase(1e14, "1e+14")]
    [TestCase(99999999999999.5, "1e+14")]
    [TestCase(double.Epsilon, "4.9406564584125e-324")]
    [TestCase(double.MaxValue, "1.7976931348623e+308")]
    [TestCase(double.PositiveInfinity, "inf")]
    [TestCase(double.NegativeInfinity, "-inf")]
    public async Task ImplicitConversionsShareThePlatformFormat(double value, string luaText)
    {
        using var state = CreateState();
        state.Environment["number"] = value;
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var results = await state.DoStringAsync(
                """
                local object=setmetatable({}, {__tostring=function() return number end})
                return tostring(number), number..'', table.concat({number}),
                       string.format('%s',number), tostring(object),
                       (string.gsub('x','x',number)),
                       (string.gsub('x','x',function() return number end)),
                       (string.gsub('x','x',{x=number}))
                """
            );
            Assert.That(
                results.Select(v => v.Read<string>()),
                Is.All.EqualTo(Expected(value, luaText))
            );
            Assert.That(
                new LuaValue(value).ToString(),
                Is.EqualTo(value.ToString(CultureInfo.InvariantCulture))
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task NaNTextPreservesSignWithoutCrtPayloadSuffixes(bool negative, bool signaling)
    {
        var bits = signaling ? 0x7ff0000000000001UL : 0x7ff8000000000000UL;
        if (negative)
            bits |= 1UL << 63;
        var value = BitConverter.Int64BitsToDouble(unchecked((long)bits));
        using var state = CreateState();
        state.Environment["number"] = value;
        var results = await state.DoStringAsync(
            "return tostring(number),number..'',table.concat({number}),string.format('%q',number)"
        );
        var text = Expected(value, negative ? "-nan" : "nan");
        Assert.That(results.Take(3).Select(v => v.Read<string>()), Is.All.EqualTo(text));
        Assert.That(results[3].Read<string>(), Is.EqualTo('"' + text + '"'));
        Assert.That(
            BitConverter.DoubleToInt64Bits(state.Environment["number"].Read<double>()),
            Is.EqualTo(unchecked((long)bits))
        );
    }

    [Test]
    public async Task QuotesAndExplicitBaseConversionUseImplicitNumericText()
    {
        using var state = CreateState();
        var result = await state.DoStringAsync(
            """
            return string.format('%+.2q',1/3), tonumber(1e14,10),
                   tonumber('100000000000000',10), tonumber('1e14'),
                   tonumber('inf'), tonumber('nan'), tonumber(123,16)
            """
        );
        var expectedNumber =
            numberFormat == LuaNumberFormat.Lua ? LuaValue.Nil : new LuaValue(1e14);
        Assert.That(
            result,
            Is.EqualTo(
                new LuaValue[]
                {
                    '"' + Expected(1d / 3, "0.33333333333333") + '"',
                    expectedNumber,
                    1e14,
                    1e14,
                    LuaValue.Nil,
                    LuaValue.Nil,
                    291,
                }
            )
        );
    }

    [Test]
    public async Task ExplicitFiniteFormatsKeepTheirRequestedPrecision()
    {
        using var state = CreateState();
        var result = await state.DoStringAsync(
            "return string.format('%f|%e|%g|%.17g|%#.4g|%.2a|%A',1/3,1/3,1/3,1/3,1,1/3,1)"
        );
        Assert.That(
            result[0].Read<string>(),
            Is.EqualTo("0.333333|3.333333e-01|0.333333|0.33333333333333331|1.000|0x1.55p-2|0X1P+0")
        );
    }

    [TestCase("f")]
    [TestCase("e")]
    [TestCase("E")]
    [TestCase("g")]
    [TestCase("G")]
    [TestCase("a")]
    [TestCase("A")]
    public async Task ExplicitSpecialFormatsHonorSignCaseAndPadding(string specifier)
    {
        using var state = CreateState();
        state.Environment["negativeNaN"] = BitConverter.Int64BitsToDouble(
            unchecked((long)0xfff8000000000000UL)
        );
        state.Environment["positiveNaN"] = BitConverter.Int64BitsToDouble(0x7ff8000000000000);
        var result = await state.DoStringAsync(
            $"return string.format('%+08{specifier}|%08{specifier}|%08{specifier}|%+08{specifier}',math.huge,-math.huge,negativeNaN,positiveNaN)"
        );
        var upper = specifier is "E" or "G" or "A";
        var infinity = numberFormat == LuaNumberFormat.Lua ? "inf" : "Infinity";
        var nan = numberFormat == LuaNumberFormat.Lua ? "nan" : "NaN";
        if (upper)
        {
            infinity = infinity.ToUpperInvariant();
            nan = nan.ToUpperInvariant();
        }
        var negativeNan = numberFormat == LuaNumberFormat.Lua ? "-" + nan : nan;
        var expected = string.Join(
            '|',
            ("+" + infinity).PadLeft(8),
            ("-" + infinity).PadLeft(8),
            negativeNan.PadLeft(8),
            ("+" + nan).PadLeft(8)
        );
        Assert.That(result[0].Read<string>(), Is.EqualTo(expected));
    }

    [TestCase(LuaStringMode.Utf16)]
    [TestCase(LuaStringMode.Bytes)]
    public async Task FileAndDefaultOutputUseThePlatformFormat(LuaStringMode stringMode)
    {
        using var state = CreateState(stringMode);
        using var buffer = new MemoryStream();
        using var stream = new LuaStream(LuaFileOpenMode.Write, buffer, stringMode);
        state.Environment["file"] = new LuaValue(new FileHandle(stream));
        await state.DoStringAsync(
            "file:write(1/3,'|',math.huge);io.output(file);io.write('|',1/3)"
        );
        var text = Expected(1d / 3, "0.33333333333333");
        Assert.That(
            Encoding.UTF8.GetString(buffer.ToArray()),
            Is.EqualTo(text + '|' + Expected(double.PositiveInfinity, "inf") + '|' + text)
        );
    }

    [Test]
    public async Task NumericInputAndOutputFilenamesUseThePlatformFormat()
    {
        var files = new RecordingFileSystem();
        using var state = LuaState.Create(
            LuaPlatform.Default with
            {
                NumberFormat = numberFormat,
                FileSystem = files,
            }
        );
        state.OpenStandardLibraries();
        await state.DoStringAsync("io.input(1/3):close();io.output(1/3):close()");
        Assert.That(
            files.Paths,
            Is.EqualTo(
                new[] { Expected(1d / 3, "0.33333333333333"), Expected(1d / 3, "0.33333333333333") }
            )
        );
    }

    sealed class RecordingFileSystem : NotImplementedExceptionFileSystemBase
    {
        public readonly List<string> Paths = new();

        public override ValueTask<ILuaStream> Open(
            string path,
            LuaFileOpenMode mode,
            CancellationToken cancellationToken
        )
        {
            Paths.Add(path);
            return new(ILuaStream.CreateFromStream(new MemoryStream(), mode));
        }
    }

    [Test]
    public async Task CoroutinesInheritTheFormatAndOtherStatesRemainIndependent()
    {
        using var state = CreateState();
        using var other = LuaState.Create();
        other.OpenStandardLibraries();
        var result = await state.DoStringAsync(
            "return coroutine.wrap(function() return tostring(1/3) end)()"
        );
        Assert.That(result[0].Read<string>(), Is.EqualTo(Expected(1d / 3, "0.33333333333333")));
        Assert.That(
            (await other.DoStringAsync("return tostring(1/3)"))[0].Read<string>(),
            Is.EqualTo((1d / 3).ToString(CultureInfo.InvariantCulture))
        );
        Assert.That(LuaPlatform.Default.NumberFormat, Is.EqualTo(LuaNumberFormat.DotNet));
    }
}
