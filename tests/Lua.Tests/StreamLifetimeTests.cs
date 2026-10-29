using System.Buffers;
using System.Text;
using Lua.IO;
using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class StreamLifetimeTests(LuaStringMode stringMode)
{
    LuaStream CreateStream(string text) =>
        new(LuaFileOpenMode.Read, new MemoryStream(Encoding.UTF8.GetBytes(text)), stringMode);

    [TestCase(3, false)]
    [TestCase(3, true)]
    [TestCase(1023, false)]
    [TestCase(1023, true)]
    [TestCase(2048, false)]
    [TestCase(2048, true)]
    public async Task BufferedLinesPreserveEndingsAndFinalLine(int length, bool keepEol)
    {
        var line = new string('x', length);
        using var stream = CreateStream(line + "\r\n\nlast é");
        var ending = stringMode == LuaStringMode.Bytes ? "\r" : "";
        Assert.That(
            await stream.ReadLineAsync(keepEol, default),
            Is.EqualTo(line + (keepEol ? "\r\n" : ending))
        );
        Assert.That(await stream.ReadLineAsync(keepEol, default), Is.EqualTo(keepEol ? "\n" : ""));
        Assert.That(
            await stream.ReadLineAsync(keepEol, default),
            Is.EqualTo(stringMode == LuaStringMode.Bytes ? "last \u00c3\u00a9" : "last é")
        );
        Assert.That(await stream.ReadLineAsync(keepEol, default), Is.Null);
    }

    [TestCase(SeekOrigin.Begin, -1)]
    [TestCase(SeekOrigin.Current, -8)]
    [TestCase(SeekOrigin.End, -8)]
    public async Task FailedSeekPreservesUnreadBytes(SeekOrigin origin, long offset)
    {
        using var stream = CreateStream("abc\ndef");
        Assert.That(await stream.ReadLineAsync(false, default), Is.EqualTo("abc"));
        Assert.Throws<IOException>(() => stream.Seek(origin, offset));
        Assert.That(await stream.ReadAllAsync(default), Is.EqualTo("def"));
    }

    [TestCase(SeekOrigin.Begin, 0, 0, "abc\ndef")]
    [TestCase(SeekOrigin.Current, 0, 4, "def")]
    [TestCase(SeekOrigin.End, -3, 4, "def")]
    public async Task SuccessfulSeekUsesTheLogicalReadPosition(
        SeekOrigin origin,
        long offset,
        long position,
        string remaining
    )
    {
        using var stream = CreateStream("abc\ndef");
        Assert.That(await stream.ReadLineAsync(false, default), Is.EqualTo("abc"));
        Assert.That(stream.Seek(origin, offset), Is.EqualTo(position));
        Assert.That(await stream.ReadAllAsync(default), Is.EqualTo(remaining));
    }

    [Test]
    public async Task LuaFileReadContinuesAfterFailedSeek()
    {
        using var state = LuaState.Create(
            stringMode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        using var stream = CreateStream("abc\ndef");
        state.Environment["file"] = new LuaValue(new FileHandle(stream));
        var values = await state.DoStringAsync(
            "local line=file:read('*l'); local pos=file:seek('set',-1); return line,pos,file:read('*a')"
        );
        Assert.That(values, Is.EqualTo(new LuaValue[] { "abc", LuaValue.Nil, "def" }));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task DisposalClosesUnderlyingStreamWhenCleanupThrows(
        bool flushFailure,
        bool disposeFailure
    )
    {
        var inner = new CleanupStream(flushFailure, disposeFailure);
        var stream = new LuaStream(LuaFileOpenMode.ReadUpdate, inner, stringMode);
        Assert.That(await stream.ReadLineAsync(false, default), Is.EqualTo("line"));
        if (flushFailure || disposeFailure)
            Assert.Throws<IOException>(stream.Dispose);
        else
            stream.Dispose();
        Assert.That(inner.DisposeCount, Is.EqualTo(1));
        Assert.That(inner.CanRead, Is.False);
        Assert.That(stream.IsOpen, Is.False);
        stream.Dispose();
        Assert.That(inner.DisposeCount, Is.EqualTo(1));
    }

    sealed class CleanupStream(bool flushFailure, bool disposeFailure)
        : MemoryStream("line\n"u8.ToArray())
    {
        public int DisposeCount { get; private set; }

        public override void Flush()
        {
            if (flushFailure)
                throw new IOException("flush failed");
        }

        protected override void Dispose(bool disposing)
        {
            DisposeCount++;
            base.Dispose(disposing);
            if (disposeFailure)
                throw new IOException("dispose failed");
        }
    }

    [TestCase(3)]
    [TestCase(2048)]
    public async Task RawByteThenBulkReadConsumeBufferedBytes(int length)
    {
        var payload = Enumerable.Range(0, length).Select(i => (byte)(255 - i % 256)).ToArray();
        var data = Encoding.ASCII.GetBytes("abc\n").Concat(payload).ToArray();
        using var stream = new LuaStream(LuaFileOpenMode.Read, new MemoryStream(data), stringMode);
        Assert.That(await stream.ReadLineAsync(false, default), Is.EqualTo("abc"));
        Assert.That(await stream.ReadByteAsync(default), Is.EqualTo(255));
        var writer = new ArrayBufferWriter<byte>();
        writer.GetSpan(1)[0] = 42;
        writer.Advance(1);
        await stream.ReadBytesAsync(writer, default);
        Assert.That(
            writer.WrittenSpan.ToArray(),
            Is.EqualTo(
                new byte[] { 42 }
                    .Concat(payload.Skip(1))
                    .ToArray()
            )
        );
        Assert.That(await stream.ReadByteAsync(default), Is.EqualTo(-1));
        Assert.That(await stream.ReadAllAsync(default), Is.Empty);
    }

    [TestCase(3)]
    [TestCase(2048)]
    public async Task RawBulkReadConsumesBufferedBytes(int length)
    {
        var payload = Enumerable.Range(0, length).Select(i => (byte)(255 - i % 256)).ToArray();
        var data = Encoding.ASCII.GetBytes("abc\n").Concat(payload).ToArray();
        using var stream = new LuaStream(LuaFileOpenMode.Read, new MemoryStream(data), stringMode);
        Assert.That(await stream.ReadLineAsync(false, default), Is.EqualTo("abc"));
        var writer = new ArrayBufferWriter<byte>();
        await stream.ReadBytesAsync(writer, default);
        Assert.That(writer.WrittenSpan.ToArray(), Is.EqualTo(payload));
        Assert.That(await stream.ReadByteAsync(default), Is.EqualTo(-1));
        Assert.That(await stream.ReadAllAsync(default), Is.Empty);
    }

    [Test]
    public async Task CancelledRawBulkReadDoesNotConsumeBufferedBytes()
    {
        using var stream = CreateStream("abc\ndef");
        Assert.That(await stream.ReadLineAsync(false, default), Is.EqualTo("abc"));
        using var source = new CancellationTokenSource();
        source.Cancel();
        var writer = new ArrayBufferWriter<byte>();
        Assert.Throws<OperationCanceledException>(() =>
        {
            _ = stream.ReadBytesAsync(writer, source.Token);
        });
        Assert.That(writer.WrittenCount, Is.Zero);
        Assert.That(await stream.ReadAllAsync(default), Is.EqualTo("def"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task BomDetectionDoesNotReadUnwrittenBufferTail(bool readAll)
    {
        await Task.Factory.StartNew(
            () =>
            {
                var rented = ArrayPool<byte>.Shared.Rent(1024);
                new byte[] { 0xEF, 0xBB, 0xBF }.CopyTo(rented, 0);
                ArrayPool<byte>.Shared.Return(rented);
                using var stream = new LuaStream(
                    LuaFileOpenMode.Read,
                    new MemoryStream(new byte[] { 0xEF }),
                    stringMode
                );
                var text = readAll
                    ? stream.ReadAllAsync(default).Result
                    : stream.ReadLineAsync(false, default).Result;
                Assert.That(
                    text,
                    Is.EqualTo(stringMode == LuaStringMode.Bytes ? "\u00EF" : "\uFFFD")
                );
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        );
    }

    [Test]
    public async Task ClosingOneReaderDoesNotReleaseAnotherReadersBuffer()
    {
        await Task.Factory.StartNew(
            () =>
            {
                using var first = CreateStream("a\nA-left\n");
                using var second = CreateStream("b\nB-left\n");
                using var third = CreateStream("c\nC-left\n");
                Assert.That(first.ReadLineAsync(false, default).Result, Is.EqualTo("a"));
                Assert.That(second.ReadLineAsync(false, default).Result, Is.EqualTo("b"));
                second.Dispose();
                Assert.That(third.ReadLineAsync(false, default).Result, Is.EqualTo("c"));
                Assert.That(first.ReadLineAsync(false, default).Result, Is.EqualTo("A-left"));
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        );
    }

    [Test]
    public async Task DisposingOnAnotherThreadPreservesThatThreadsReader()
    {
        var first = await Task.Factory.StartNew(
            () =>
            {
                var stream = CreateStream("a\nA-left\n");
                Assert.That(stream.ReadLineAsync(false, default).Result, Is.EqualTo("a"));
                return stream;
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        );
        await Task.Factory.StartNew(
            () =>
            {
                using (first)
                {
                    using var second = CreateStream("b\nB-left\n");
                    using var third = CreateStream("c\nC-left\n");
                    Assert.That(second.ReadLineAsync(false, default).Result, Is.EqualTo("b"));
                    first.Dispose();
                    Assert.That(third.ReadLineAsync(false, default).Result, Is.EqualTo("c"));
                    Assert.That(second.ReadLineAsync(false, default).Result, Is.EqualTo("B-left"));
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        );
    }
}
