using Lua.IO;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(false)]
[TestFixture(true)]
public class MemoryStreamReadTests(bool stringBacked)
{
    ILuaStream Create(string text) =>
        stringBacked
            ? ILuaStream.CreateFromString(text)
            : ILuaStream.CreateFromMemory(text.AsMemory());

    [Test]
    public async Task ReadAllConsumesWholeAndRemainingContent()
    {
        using var stream = Create("abc");
        Assert.That(await stream.ReadAllAsync(default), Is.EqualTo("abc"));
        Assert.That(stream.Seek(SeekOrigin.Current, 0), Is.EqualTo(3));
        Assert.That(await stream.ReadAsync(1, default), Is.Null);
        Assert.That(await stream.ReadAllAsync(default), Is.Empty);
        stream.Seek(SeekOrigin.Begin, 0);
        Assert.That(await stream.ReadAsync(1, default), Is.EqualTo("a"));
        Assert.That(await stream.ReadAllAsync(default), Is.EqualTo("bc"));
        Assert.That(await stream.ReadAllAsync(default), Is.Empty);
    }

    [TestCase("x12", "x12")]
    [TestCase("  x12", "x12")]
    [TestCase(" \t\r\n", "")]
    [TestCase("", "")]
    public async Task FailedNumericReadsConsumeOnlyLeadingWhitespace(string input, string remainder)
    {
        using var stream = Create(input);
        Assert.That(await stream.ReadNumberAsync(default), Is.Null);
        Assert.That(await stream.ReadNumberAsync(default), Is.Null);
        Assert.That(
            stream.Seek(SeekOrigin.Current, 0),
            Is.EqualTo(input.Length - remainder.Length)
        );
        Assert.That(await stream.ReadAllAsync(default), Is.EqualTo(remainder));
    }

    [TestCase(" 12 rest", 12d)]
    [TestCase(" -3.5 rest", -3.5)]
    [TestCase(" 0x1.fp2 rest", 7.75)]
    public async Task SuccessfulNumericReadsPreserveFollowingText(string input, double expected)
    {
        using var stream = Create(input);
        Assert.That(await stream.ReadNumberAsync(default), Is.EqualTo(expected));
        Assert.That(await stream.ReadAllAsync(default), Is.EqualTo(" rest"));
    }

    [Test]
    public async Task LuaFileReadsFollowTheSameCursorRules()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        using var whole = Create("abc");
        using var numeric = Create("  x12");
        state.Environment["whole"] = new LuaValue(new FileHandle(whole));
        state.Environment["numeric"] = new LuaValue(new FileHandle(numeric));
        var result = await state.DoStringAsync(
            "return whole:read('*a'),whole:read(1),whole:read('*a'),numeric:read('*n'),numeric:read('*a')"
        );
        Assert.That(
            result,
            Is.EqualTo(new LuaValue[] { "abc", LuaValue.Nil, "", LuaValue.Nil, "x12" })
        );
    }
}
