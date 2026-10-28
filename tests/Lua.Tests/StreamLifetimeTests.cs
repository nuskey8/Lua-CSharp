using System.Buffers;
using System.Text;
using Lua.IO;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class StreamLifetimeTests(LuaStringMode stringMode)
{
    LuaStream CreateStream(string text) =>
        new(LuaFileOpenMode.Read, new MemoryStream(Encoding.UTF8.GetBytes(text)), stringMode);

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
