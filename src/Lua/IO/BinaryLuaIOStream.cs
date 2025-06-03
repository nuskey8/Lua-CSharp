namespace Lua.IO;

internal sealed class BinaryLuaIOStream(LuaFileOpenMode mode, Stream innerStream) : ILuaIOStream
{
    ulong flushSize = ulong.MaxValue;
    ulong nextFlushSize = ulong.MaxValue;

    public LuaFileOpenMode Mode => mode;
    public LuaFileContentType ContentType => LuaFileContentType.Bytes;

    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Cannot read lines from a binary stream. Use a text stream instead.");
    }

    public ValueTask<LuaFileContent> ReadToEndAsync(CancellationToken cancellationToken)
    {
        ThrowIfNotReadable();
        using var memoryStream = new MemoryStream();
        innerStream.CopyTo(memoryStream);
        var bytes = memoryStream.ToArray();
        return new(new LuaFileContent(bytes,bytes.Length));
    }

    public ValueTask<string?> ReadStringAsync(int count, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Cannot read strings from a binary stream. Use a text stream instead.");
    }

    public ValueTask WriteAsync(LuaFileContent content, CancellationToken cancellationToken)
    {
        if (content.Type != LuaFileContentType.Bytes)
        {
            throw new InvalidOperationException("Cannot write string to a binary stream.");
        }
        
        return WriteBytesAsync(content.ReadBytes().Span, cancellationToken);
    }
    

    public ValueTask<byte[]?> ReadBytesAsync(int count, CancellationToken cancellationToken)
    {
        ThrowIfNotReadable();
        
        if (count <= 0) return new ValueTask<byte[]?>((byte[]?)null);
        
        var buffer = new byte[count];
        var totalRead = 0;
        
        while (totalRead < count)
        {
            var bytesRead = innerStream.Read(buffer, totalRead, count - totalRead);
            if (bytesRead == 0) break; // End of stream
            totalRead += bytesRead;
        }
        
        if (totalRead == 0) return new ValueTask<byte[]?>((byte[]?)null);
        if (totalRead < count)
        {
            Array.Resize(ref buffer, totalRead);
        }
        
        return new ValueTask<byte[]?>(buffer);
    }


    public ValueTask WriteBytesAsync(ReadOnlySpan<byte> buffer, CancellationToken cancellationToken)
    {
        ThrowIfNotWritable();
        
        if (mode is LuaFileOpenMode.Append or LuaFileOpenMode.ReadAppend)
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

    void ThrowIfNotReadable()
    {
        if (!innerStream.CanRead)
        {
            throw new IOException("Stream is not readable.");
        }
    }

    void ThrowIfNotWritable()
    {
        if (!innerStream.CanWrite)
        {
            throw new IOException("Stream is not writable.");
        }
    }

    public void Dispose()
    {
        if (innerStream.CanWrite) innerStream.Flush();
        innerStream.Dispose();
    }
}