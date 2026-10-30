using System.Text;
using Lua.IO;
using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class Utf8StreamReadTests(LuaStringMode mode)
{
    string Expected(byte[] bytes) =>
        mode == LuaStringMode.Bytes
            ? LuaByteString.FromBytes(bytes)
            : Encoding.UTF8.GetString(bytes);

    LuaStream Create(byte[] bytes, int chunkSize = int.MaxValue) =>
        new(LuaFileOpenMode.Read, new ShortReadStream(bytes, chunkSize), mode);

    static ValueTask<string?> Read(LuaStream stream, string format) =>
        format switch
        {
            "line" => stream.ReadLineAsync(false, default),
            "count" => stream.ReadAsync(10000, default),
            _ => ReadAll(stream),
        };

    static async ValueTask<string?> ReadAll(LuaStream stream) => await stream.ReadAllAsync(default);

    [Test, Combinatorial]
    public async Task OnlyTheInitialBomIsSkipped(
        [Values("line", "count", "all")] string format,
        [Values(false, true)] bool leadingBom
    )
    {
        var content = new string('a', leadingBom ? 1021 : 1024) + "\uFEFFz";
        var bytes = Encoding.UTF8.GetBytes((leadingBom ? "\uFEFF" : "") + content);
        using var stream = Create(bytes);
        var expected = mode == LuaStringMode.Bytes ? Expected(bytes) : content;
        Assert.That(await Read(stream, format), Is.EqualTo(expected));
        stream.Seek(SeekOrigin.Begin, 0);
        Assert.That(await Read(stream, format), Is.EqualTo(expected));
    }

    [Test, Combinatorial]
    public async Task InitialBomSurvivesShortReadsAndEofProbes(
        [Values("line", "count", "all")] string format,
        [Values(1, 2, 3)] int chunkSize
    )
    {
        var bytes = Encoding.UTF8.GetBytes("\uFEFF42");
        using var stream = Create(bytes, chunkSize);
        Assert.That(await stream.ReadAsync(0, default), Is.EqualTo(""));
        Assert.That(
            await Read(stream, format),
            Is.EqualTo(mode == LuaStringMode.Bytes ? Expected(bytes) : "42")
        );
    }

    [Test]
    public async Task SeekingIntoTheFileDoesNotStripItsBomCharacter()
    {
        var bytes = Encoding.UTF8.GetBytes("a\uFEFFb");
        using var stream = Create(bytes);
        stream.Seek(SeekOrigin.Begin, 1);
        Assert.That(await stream.ReadAllAsync(default), Is.EqualTo(Expected(bytes[1..])));
    }

    [Test, Combinatorial]
    public async Task CountedReadsPreserveUnicodeAcrossSmallBuffers(
        [Values(1, 2, 3)] int count,
        [Values(1, 1024)] int chunkSize
    )
    {
        var bytes = Encoding.UTF8.GetBytes("a😀bế𐀀z");
        using var stream = Create(bytes, chunkSize);
        var result = new StringBuilder();
        while (await stream.ReadAsync(count, default) is { } text)
        {
            Assert.That(text.Length, Is.InRange(1, count));
            result.Append(text);
        }
        Assert.That(result.ToString(), Is.EqualTo(Expected(bytes)));
    }

    [Test]
    public async Task LuaCountedReadsCanIterateEmoji()
    {
        using var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        var bytes = Encoding.UTF8.GetBytes("😀x");
        using var stream = Create(bytes);
        state.Environment["file"] = new LuaValue(new FileHandle(stream));
        var result = await state.DoStringAsync(
            "local t={};for c in file:lines(1) do t[#t+1]=c end;return table.concat(t)"
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { Expected(bytes) }));
    }

    [TestCase("line")]
    [TestCase("all")]
    [TestCase("number")]
    public async Task OtherFormatsPreserveThePendingCharacter(string format)
    {
        var bytes = Encoding.UTF8.GetBytes("😀12\n34");
        using var stream = Create(bytes);
        var first = await stream.ReadAsync(1, default);
        Assert.That(await stream.ReadAsync(0, default), Is.EqualTo(""));
        if (format == "number")
            Assert.That(await stream.ReadNumberAsync(default), Is.Null);
        if (format == "line")
        {
            Assert.That(
                first + await stream.ReadLineAsync(false, default),
                Is.EqualTo(Expected(bytes[..^3]))
            );
            Assert.That(await stream.ReadNumberAsync(default), Is.EqualTo(34));
        }
        else
            Assert.That(first + await stream.ReadAllAsync(default), Is.EqualTo(Expected(bytes)));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(20)]
    public async Task EofFlushesTheDecoderAndSeekingResetsIt(int count)
    {
        byte[] bytes = [(byte)'a', 0xe2, 0x82];
        using var stream = Create(bytes, 1);
        for (var pass = 0; pass < 2; pass++)
        {
            var result = new StringBuilder();
            while (await stream.ReadAsync(count, default) is { } text)
                result.Append(text);
            Assert.That(result.ToString(), Is.EqualTo(Expected(bytes)));
            stream.Seek(SeekOrigin.Begin, 0);
        }
    }

    [Test]
    public async Task PendingCharactersRemainReadableAtEofAndAfterPositionQueries()
    {
        var bytes = Encoding.UTF8.GetBytes("😀");
        using var stream = Create(bytes);
        var first = await stream.ReadAsync(1, default);
        Assert.That(
            stream.Seek(SeekOrigin.Current, 0),
            Is.EqualTo(mode == LuaStringMode.Bytes ? 1 : 4)
        );
        Assert.That(await stream.ReadAsync(0, default), Is.EqualTo(""));
        Assert.That(first + await stream.ReadAllAsync(default), Is.EqualTo(Expected(bytes)));
        Assert.That(await stream.ReadAsync(0, default), Is.Null);
    }

    [TestCase("line")]
    [TestCase("all")]
    [TestCase("number")]
    public async Task FormatChangesPreserveFallbackAndFollowingCharacters(string format)
    {
        var tail = format == "line" ? "\nrest" : "7 rest";
        var bytes = new byte[] { 0xe2, 0x82 }
            .Concat(Encoding.UTF8.GetBytes(tail))
            .ToArray();
        using var stream = Create(bytes);
        var result = await stream.ReadAsync(1, default);
        if (format == "line")
            result += await stream.ReadLineAsync(false, default) + "\n";
        if (format == "number")
        {
            var number = await stream.ReadNumberAsync(default);
            Assert.That(number, Is.EqualTo(mode == LuaStringMode.Bytes ? (double?)null : 7d));
            if (number.HasValue)
                result += "7";
        }
        result += await stream.ReadAllAsync(default);
        Assert.That(result, Is.EqualTo(Expected(bytes)));
    }

    [Test]
    public async Task PositionQueriesSynchronizeTheCursorBeforeWriting()
    {
        using var stream = new LuaStream(
            LuaFileOpenMode.ReadUpdate,
            new MemoryStream("abc"u8.ToArray()),
            mode
        );
        Assert.That(await stream.ReadAsync(1, default), Is.EqualTo("a"));
        Assert.That(stream.Seek(SeekOrigin.Current, 0), Is.EqualTo(1));
        await stream.WriteAsync("x".AsMemory(), default);
        stream.Seek(SeekOrigin.Begin, 0);
        Assert.That(await stream.ReadAllAsync(default), Is.EqualTo("axc"));
    }

    [Test]
    public async Task FailedSeekPreservesThePendingCharacter()
    {
        var bytes = Encoding.UTF8.GetBytes("😀x");
        using var stream = Create(bytes);
        var first = await stream.ReadAsync(1, default);
        Assert.Throws<IOException>(() => stream.Seek(SeekOrigin.Begin, -1));
        Assert.That(first + await stream.ReadAllAsync(default), Is.EqualTo(Expected(bytes)));
    }

    sealed class ShortReadStream(byte[] bytes, int chunkSize) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            base.Read(buffer, offset, Math.Min(count, chunkSize));
    }
}
