using Lua.Internal;
using System.Text;

namespace Lua.IO;

sealed class LuaStream(LuaFileOpenMode mode, Stream innerStream) : ILuaStream
{
    Utf8Reader? reader;
    ulong flushSize = ulong.MaxValue;
    ulong nextFlushSize = ulong.MaxValue;
    bool disposed;

    public LuaFileOpenMode Mode => mode;

    public bool IsOpen => !disposed;

    public ValueTask<string?> ReadLineAsync(bool keepEol, CancellationToken cancellationToken)
    {
        mode.ThrowIfNotReadable();
        reader ??= new();
        return new(reader.ReadLine(innerStream, keepEol));
    }

    public ValueTask<string> ReadAllAsync(CancellationToken cancellationToken)
    {
        mode.ThrowIfNotReadable();
        reader ??= new();
        var text = reader.ReadToEnd(innerStream);
        return new(text);
    }

    public ValueTask<string?> ReadAsync(int count, CancellationToken cancellationToken)
    {
        mode.ThrowIfNotReadable();
        reader ??= new();
        return new(reader.Read(innerStream, count));
    }

    public ValueTask<double?> ReadNumberAsync(CancellationToken cancellationToken)
    {
        mode.ThrowIfNotReadable();
        reader ??= new();

        // Use the Utf8Reader's ReadNumber method which handles positioning correctly
        var numberStr = reader.ReadNumber(innerStream);
        if (numberStr == null)
        {
            return new((double?)null);
        }

        // Parse using the shared utility
        var result = NumberReaderHelper.ParseNumber(numberStr.AsSpan());
        return new(result);
    }


    public ValueTask WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken)
    {
        if (mode.IsAppend())
        {
            innerStream.Seek(0, SeekOrigin.End);
        }

        using PooledArray<byte> byteBuffer = new(4096);
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

    public long Seek(SeekOrigin origin, long offset)
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
        if (disposed)
        {
            return;
        }

        disposed = true;

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