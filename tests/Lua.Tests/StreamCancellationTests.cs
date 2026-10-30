using System.Text;
using Lua.IO;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class StreamCancellationTests(LuaStringMode mode)
{
    static async Task Read(LuaStream stream, string format, CancellationToken token)
    {
        switch (format)
        {
            case "line":
                await stream.ReadLineAsync(false, token);
                break;
            case "all":
                await stream.ReadAllAsync(token);
                break;
            case "number":
                await stream.ReadNumberAsync(token);
                break;
            case "byte":
                await stream.ReadByteAsync(token);
                break;
            default:
                await stream.ReadAsync(int.Parse(format), token);
                break;
        }
    }

    [TestCase("line")]
    [TestCase("all")]
    [TestCase("number")]
    [TestCase("byte")]
    [TestCase("0")]
    [TestCase("10000")]
    public async Task AlreadyCancelledReadsPreserveBufferedData(string format)
    {
        using var stream = new LuaStream(
            LuaFileOpenMode.Read,
            new MemoryStream("first\n12 rest"u8.ToArray()),
            mode
        );
        Assert.That(await stream.ReadLineAsync(false, default), Is.EqualTo("first"));
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(() => Read(stream, format, source.Token));
        Assert.That(await stream.ReadAllAsync(default), Is.EqualTo("12 rest"));
    }

    [TestCase("line")]
    [TestCase("all")]
    [TestCase("number")]
    [TestCase("10000")]
    public void CancellationStopsReadsAtTheNextBufferBoundary(string format)
    {
        using var source = new CancellationTokenSource();
        var text = format == "number" ? new string(' ', 10000) + "12" : new string('x', 10000);
        using var inner = new CancellingStream(Encoding.UTF8.GetBytes(text), source);
        using var stream = new LuaStream(LuaFileOpenMode.Read, inner, mode);
        Assert.ThrowsAsync<OperationCanceledException>(() => Read(stream, format, source.Token));
        Assert.That(inner.ReadCount, Is.EqualTo(1));
    }

    sealed class CancellingStream(byte[] bytes, CancellationTokenSource source)
        : MemoryStream(bytes)
    {
        public int ReadCount { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCount++;
            source.Cancel();
            return base.Read(buffer, offset, Math.Min(count, 100));
        }
    }
}
