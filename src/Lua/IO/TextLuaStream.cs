using Lua.Internal;
using System.Text;

namespace Lua.IO;

internal sealed class TextLuaStream(LuaFileMode mode, Stream innerStream) : ILuaStream
{
    Utf8Reader? reader;
    ulong flushSize = ulong.MaxValue;
    ulong nextFlushSize = ulong.MaxValue;

    public LuaFileMode Mode => mode;

    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        mode.ThrowIfNotReadable();
        reader ??= new();
        return new(reader.ReadLine(innerStream));
    }

    public ValueTask<LuaFileContent> ReadAllAsync(CancellationToken cancellationToken)
    {
        mode.ThrowIfNotReadable();
        reader ??= new();
        var text = reader.ReadToEnd(innerStream);
        return new(new LuaFileContent(text));
    }

    public ValueTask<string?> ReadStringAsync(int count, CancellationToken cancellationToken)
    {
        mode.ThrowIfNotReadable();
        reader ??= new();
        return new(reader.Read(innerStream, count));
    }

    public ValueTask WriteAsync(LuaFileContent content, CancellationToken cancellationToken)
    {
        mode.ThrowIfNotWritable();
        return WriteAsync(content.ReadText(), cancellationToken);
    }

    public ValueTask WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken)
    {
        if (mode.IsAppend())
        {
            innerStream.Seek(0, SeekOrigin.End);
        }

        using var byteBuffer = new PooledArray<byte>(4096);
        var encoder = Encoding.UTF8.GetEncoder();
        var totalBytes = encoder.GetByteCount(buffer.Span, true);
        var remainingBytes = totalBytes;
        while (0 < remainingBytes)
        {
            var byteCount = encoder.GetBytes(buffer.Span, byteBuffer.AsSpan(), false);
            innerStream.Write(byteBuffer.AsSpan()[..byteCount]);
            remainingBytes -= byteCount;
        }

        if (nextFlushSize < (ulong)totalBytes)
        {
            innerStream.Flush();
            nextFlushSize = flushSize;
        }

        reader?.Clear();
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
        if (reader != null && origin == SeekOrigin.Current)
        {
            offset -= reader.Remain;
        }

        reader?.Clear();
        return innerStream.Seek(offset, origin);
    }

    public void Dispose()
    {
        try
        {
            if (innerStream.CanWrite)
            {
                innerStream.Flush();
            }

            innerStream.Dispose();
        }
        finally
        {
            reader?.Dispose();
        }
    }
}