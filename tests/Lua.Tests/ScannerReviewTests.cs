using Lua.Platforms;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class ScannerReviewTests(LuaStringMode mode)
{
    LuaState CreateState() =>
        LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );

    [TestCase("''", "")]
    [TestCase("\"\"", "")]
    [TestCase("'plain text'", "plain text")]
    [TestCase("\"single ' quote\"", "single ' quote")]
    [TestCase("'double \" quote'", "double \" quote")]
    [TestCase("'embedded\0byte'", "embedded\0byte")]
    [TestCase("'\\a\\b\\f\\n\\r\\t\\v\\\\\\\"\\\''", "\a\b\f\n\r\t\v\\\"'")]
    [TestCase("'\\0\\00\\0001\\255\\x00\\xFf'", "\0\0\x0000" + "1\xff\0\xff")]
    [TestCase("'a\\\nb\\\r\nc\\\n\rd\\\re'", "a\nb\nc\nd\ne")]
    [TestCase("'a\\z \t\r\n \v\f\nb'", "ab")]
    public async Task QuotedStringsKeepTheirDecodedValuesAndBytecode(
        string literal,
        string expected
    )
    {
        using var state = CreateState();
        var closure = state.Load("return " + literal, "scanner-strings");
        foreach (
            var executable in new[] { closure, state.Load(closure.Proto.ToBytecode(), "roundtrip") }
        )
            Assert.That(
                (await state.ExecuteAsync(executable))[0].Read<string>(),
                Is.EqualTo(expected)
            );
    }

    [TestCase("'unfinished")]
    [TestCase("'unfinished\\")]
    [TestCase("'raw\nnewline'")]
    [TestCase("'\\q'")]
    [TestCase("'\\256'")]
    [TestCase("'\\x0'")]
    [TestCase("'\\xGG'")]
    public void InvalidQuotedStringsAreStillRejected(string literal)
    {
        using var state = CreateState();
        Assert.Throws<LuaCompileException>(() =>
            state.Load("return " + literal, "scanner-invalid")
        );
    }

    [TestCase("local\n123", "123")]
    [TestCase("local\r\n123", "123")]
    [TestCase("local\n\r123", "123")]
    [TestCase("local --comment\n123", "123")]
    [TestCase("local --[[comment]]123", "123")]
    [TestCase("local --[=[a\r\nb]=]123", "123")]
    [TestCase("return 1 --[[comment]]name", "name")]
    [TestCase("local 0x10", "0x10")]
    [TestCase("local 0x1p2", "0x1p2")]
    [TestCase("local 0x.8p-1", "0x.8p-1")]
    public void DiagnosticsQuoteTheOffendingToken(string source, string token)
    {
        using var state = CreateState();
        var error = Assert.Throws<LuaCompileException>(() =>
            state.Load(source, "scanner-diagnostics")
        )!;
        Assert.That(error.MessageWithNearToken, Does.EndWith("near '" + token + "'"));
    }

    [Test]
    public async Task NamesAfterCommentsAndNewlinesKeepTheirFullSpelling()
    {
        using var state = CreateState();
        var result = await state.DoStringAsync(
            "local\nfirstName=11; local --[[comment]]secondName=31; return\nfirstName+--[=[a\r\nb]=]secondName"
        );
        Assert.That(result[0].Read<double>(), Is.EqualTo(42));
    }

    [TestCase("1and true", "1a")]
    [TestCase(".1and true", ".1a")]
    [TestCase("1e1and true", "1e1a")]
    [TestCase("0x1p2and true", "0x1p2a")]
    [TestCase("123abc", "123abc")]
    [TestCase("1.2.3", "1.2.3")]
    [TestCase("0x1..2", "0x1..2")]
    [TestCase("0x1p+", "0x1p+")]
    [TestCase(".0x1", ".0x1")]
    public void MalformedNumeralsMatchLua52TokenBoundaries(string expression, string numeral)
    {
        using var state = CreateState();
        var error = Assert.Throws<LuaCompileException>(() =>
            state.Load("return " + expression, "scanner-numeral")
        )!;
        Assert.That(
            error.MessageWithNearToken,
            Is.EqualTo("malformed number near '" + numeral + "'")
        );
    }

    [TestCase("1or 2", 1)]
    [TestCase("0x1p2or 3", 4)]
    [TestCase(".5", 0.5)]
    [TestCase("0.5e+2", 50)]
    [TestCase("0X.8P+2", 2)]
    [TestCase("000001.25", 1.25)]
    [TestCase("0x1p18446744073709551616", 1)]
    public async Task ValidNumeralBoundariesAndExponentsStillWork(
        string expression,
        double expected
    )
    {
        using var state = CreateState();
        Assert.That(
            (await state.DoStringAsync("return " + expression))[0].Read<double>(),
            Is.EqualTo(expected)
        );
    }
}
