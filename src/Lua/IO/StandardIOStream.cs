namespace Lua.IO
{
    /// <summary>
    /// Wrapper for standard IO streams that prevents closing
    /// </summary>
    internal sealed class StandardIOStream(ILuaStream innerStream) : ILuaStream
    {
        public LuaFileOpenMode Mode => innerStream.Mode;
        public bool IsOpen => innerStream.IsOpen;

        public ValueTask<string> ReadAllAsync(CancellationToken cancellationToken)
            => innerStream.ReadAllAsync(cancellationToken);

        public ValueTask<string?> ReadLineAsync(bool keepEol, CancellationToken cancellationToken)
            => innerStream.ReadLineAsync(keepEol, cancellationToken);

        public ValueTask<string?> ReadAsync(int count, CancellationToken cancellationToken)
            => innerStream.ReadAsync(count, cancellationToken);

        public ValueTask<double?> ReadNumberAsync(CancellationToken cancellationToken)
            => innerStream.ReadNumberAsync(cancellationToken);

        public ValueTask WriteAsync(ReadOnlyMemory<char> content, CancellationToken cancellationToken)
            => innerStream.WriteAsync(content, cancellationToken);

        public ValueTask FlushAsync(CancellationToken cancellationToken)
            => innerStream.FlushAsync(cancellationToken);

        public void SetVBuf(LuaFileBufferingMode mode, int size)
            => innerStream.SetVBuf(mode, size);

        public long Seek(SeekOrigin origin, long offset)
            => innerStream.Seek(origin, offset);

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
}