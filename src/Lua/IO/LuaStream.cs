using System.Buffers;
using Lua.Internal;
using System.Text;

namespace Lua.IO;

public sealed class LuaStream(LuaFileOpenMode mode, Stream innerStream) : ILuaStream, ILuaByteStream
{
    Utf8Reader? reader;
    ulong flushSize = ulong.MaxValue;
    ulong nextFlushSize = ulong.MaxValue;
    LuaFileBufferingMode bufferingMode = LuaFileBufferingMode.FullBuffering;
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

    public ValueTask ReadBytesAsync(IBufferWriter<byte> writer, CancellationToken cancellationToken)
    {
        mode.ThrowIfNotReadable();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var buffer = writer.GetSpan(4096);
            var read = innerStream.Read(buffer);
            if (read == 0)
            {
                return default;
            }

            writer.Advance(read);
        }
    }

    public ValueTask<int> ReadByteAsync(CancellationToken cancellationToken)
    {
        mode.ThrowIfNotReadable();
        return new(innerStream.ReadByte());
    }

    public ValueTask<string?> ReadAsync(int count, CancellationToken cancellationToken)
    {
        mode.ThrowIfNotReadable();
        reader ??= new();
        if (count == 0)
        {
            return new(reader.IsEndOfStream(innerStream) ? null : string.Empty);
        }

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
        mode.ThrowIfNotWritable();

        if (mode.IsAppend())
        {
            innerStream.Seek(0, SeekOrigin.End);
        }

        using PooledArray<byte> byteBuffer = new(4096);
        var encoder = Encoding.UTF8.GetEncoder();
        var remainingChars = buffer.Span;
        var totalBytes = 0;
        while (!remainingChars.IsEmpty)
        {
            encoder.Convert(
                remainingChars,
                byteBuffer.AsSpan(),
                flush: true,
                out var charsUsed,
                out var bytesUsed,
                out _);

            if (bytesUsed > 0)
            {
                innerStream.Write(byteBuffer.AsSpan()[..bytesUsed]);
                totalBytes += bytesUsed;
            }

            remainingChars = remainingChars[charsUsed..];
        }

        if (bufferingMode == LuaFileBufferingMode.NoBuffering)
        {
            innerStream.Flush();
            nextFlushSize = flushSize;
        }
        else if (bufferingMode == LuaFileBufferingMode.LineBuffering)
        {
            if (buffer.Span.IndexOf('\n') >= 0)
            {
                innerStream.Flush();
                nextFlushSize = flushSize;
            }
        }
        else if (nextFlushSize < (ulong)totalBytes)
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
        bufferingMode = mode;
        // Ignore size parameter
        if (mode is LuaFileBufferingMode.NoBuffering)
        {
            nextFlushSize = 0;
            flushSize = 0;
        }
        else if (mode is LuaFileBufferingMode.LineBuffering)
        {
            nextFlushSize = ulong.MaxValue;
            flushSize = ulong.MaxValue;
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