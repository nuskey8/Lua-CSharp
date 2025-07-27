namespace Lua.IO;

/// <summary>
/// Wrapper for standard IO streams that prevents closing
/// </summary>
sealed class StandardIOStream(ILuaStream innerStream) : ILuaStream
{
    public LuaFileOpenMode Mode => innerStream.Mode;

    public bool IsOpen => innerStream.IsOpen;

    public ValueTask<string> ReadAllAsync(CancellationToken cancellationToken)
    {
        return innerStream.ReadAllAsync(cancellationToken);
    }

    public ValueTask<string?> ReadLineAsync(bool keepEol, CancellationToken cancellationToken)
    {
        return innerStream.ReadLineAsync(keepEol, cancellationToken);
    }

    public ValueTask<string?> ReadAsync(int count, CancellationToken cancellationToken)
    {
        return innerStream.ReadAsync(count, cancellationToken);
    }

    public ValueTask<double?> ReadNumberAsync(CancellationToken cancellationToken)
    {
        return innerStream.ReadNumberAsync(cancellationToken);
    }

    public ValueTask WriteAsync(ReadOnlyMemory<char> content, CancellationToken cancellationToken)
    {
        return innerStream.WriteAsync(content, cancellationToken);
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        return innerStream.FlushAsync(cancellationToken);
    }

    public void SetVBuf(LuaFileBufferingMode mode, int size)
    {
        innerStream.SetVBuf(mode, size);
    }

    public long Seek(SeekOrigin origin, long offset)
    {
        return innerStream.Seek(origin, offset);
    }

    public void Close()
    {
        throw new IOException("cannot close standard file");
    }

    public ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        throw new IOException("cannot close standard file");
    }

    public void Dispose()
    {
        // Do not dispose inner stream to prevent closing standard IO streams
        innerStream.Dispose();
    }
}