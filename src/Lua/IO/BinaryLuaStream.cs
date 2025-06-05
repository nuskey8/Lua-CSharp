using System.Text;

namespace Lua.IO;

internal sealed class BinaryLuaStream(LuaFileMode mode, Stream innerStream) : ILuaStream
{
    ulong flushSize = ulong.MaxValue;
    ulong nextFlushSize = ulong.MaxValue;

    public LuaFileMode Mode => mode;

    public ValueTask<LuaFileContent> ReadAllAsync(CancellationToken cancellationToken)
    {
        mode.ThrowIfNotReadable();
        using var memoryStream = new MemoryStream();
        innerStream.CopyTo(memoryStream);
        var bytes = memoryStream.ToArray();
        return new(new LuaFileContent(bytes));
    }

    public ValueTask WriteAsync(LuaFileContent content, CancellationToken cancellationToken)
    {
        mode.ThrowIfNotReadable();
        if (content.Type != LuaFileContentType.Binary)
        {
            var encoding = Encoding.UTF8;
            var span = content.ReadText().Span;
            var byteCount = encoding.GetByteCount(span);
            var bytes = new byte[byteCount];
            encoding.GetBytes(span, bytes);
            return WriteBytesAsync(bytes, cancellationToken);
        }

        return WriteBytesAsync(content.ReadBytes().Span, cancellationToken);
    }


    public ValueTask WriteBytesAsync(ReadOnlySpan<byte> buffer, CancellationToken cancellationToken)
    {
        if (mode.IsAppend())
        {
            innerStream.Seek(0, SeekOrigin.End);
        }

        innerStream.Write(buffer);

        if (nextFlushSize < (ulong)buffer.Length)
        {
            innerStream.Flush();
            nextFlushSize = flushSize;
        }

        return new();
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        innerStream.Flush();
        nextFlushSize = flushSize;
        return new();
    }

    public void SetVBuf(LuaFileBufferingMode mode, int size)
    {
        // Ignore size parameter
        if (mode is LuaFileBufferingMode.NoBuffering or LuaFileBufferingMode.LineBuffering)
        {
            nextFlushSize = 0;
            flushSize = 0;
        }
        else
        {
            nextFlushSize = (ulong)size;
            flushSize = (ulong)size;
        }
    }

    public long Seek(long offset, SeekOrigin origin)
    {
        return innerStream.Seek(offset, origin);
    }

    public void Dispose()
    {
        if (innerStream.CanWrite) innerStream.Flush();
        innerStream.Dispose();
    }
}