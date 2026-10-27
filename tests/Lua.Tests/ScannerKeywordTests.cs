using Lua.CodeAnalysis.Compilation;
using Lua.Platforms;

namespace Lua.Tests;

public class ScannerKeywordTests
{
    [Test]
    public void ReservedSpellingsKeepTheirTokenNumbersAndText()
    {
        using var pool = new StringInternPool();
        var scanner = new Scanner { StringPool = pool, Current = ' ' };
        for (var i = 0; i < Scanner.Tokens.Length; i++)
        {
            var text = Scanner.Tokens[i];
            scanner.R.Position = 7 + text.Length;
            var token = scanner.ReservedOrName(7, text.AsSpan());
            Assert.That(Scanner.IsReserved(text), Is.True, text);
            Assert.That(token.T, Is.EqualTo(Scanner.FirstReserved + i), text);
            Assert.That(token.S, Is.SameAs(text), text);
            Assert.That(token.Pos, Is.EqualTo(7));
            Assert.That(token.RawLength, Is.EqualTo(text.Length));
        }
    }

    [TestCase(LuaStringMode.Utf16)]
    [TestCase(LuaStringMode.Bytes)]
    public async Task KeywordNearMissesRemainDistinctIdentifiers(LuaStringMode mode)
    {
        using var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        var names = Scanner
            .Tokens[..Scanner.ReservedCount]
            .ToArray()
            .SelectMany(word =>
                new[]
                {
                    word + "_",
                    "_" + word,
                    word.ToUpperInvariant(),
                    word + "Value123",
                    word + word,
                }
            )
            .Concat(
                new[]
                {
                    "a",
                    "i",
                    "ret",
                    "retur",
                    "returm",
                    "functions",
                    "ordinaryIdentifierLongerThanAnyKeyword",
                }
            )
            .ToArray();
        var fields = string.Join(",", names.Select((name, i) => $"{name}={i + 1}"));
        var sum = string.Join("+", names.Select(name => "t." + name));
        var result = await state.DoStringAsync($"local t={{{fields}}}; return {sum}");
        Assert.That(result[0].Read<double>(), Is.EqualTo(names.Length * (names.Length + 1) / 2));
        foreach (var name in names)
            Assert.That(Scanner.IsReserved(name), Is.False, name);
        Assert.That(Scanner.IsReserved(""), Is.False);
    }

    [TestCase("break", "<break> at line 1 not inside a loop")]
    [TestCase("goto missing", "no visible label 'missing' for <goto> at line 1")]
    public void UnresolvedJumpDiagnosticsKeepTheirClassification(string source, string message)
    {
        using var state = LuaState.Create();
        var error = Assert.Throws<LuaCompileException>(() => state.Load(source, "keywords"))!;
        Assert.That(error.MessageWithNearToken, Does.Contain(message));
    }
}
