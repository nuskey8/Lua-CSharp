namespace Lua.IO
{
    /// <summary>
    /// Wrapper for standard IO streams that prevents closing
    /// </summary>
    internal sealed class StandardIOStream(ILuaStream innerStream) : ILuaStream
    {
        public LuaFileMode Mode => innerStream.Mode;

        public ValueTask<LuaFileContent> ReadAllAsync(CancellationToken cancellationToken)
            => innerStream.ReadAllAsync(cancellationToken);

        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
            => innerStream.ReadLineAsync(cancellationToken);

        public ValueTask<string?> ReadStringAsync(int count, CancellationToken cancellationToken)
            => innerStream.ReadStringAsync(count, cancellationToken);

        public ValueTask WriteAsync(LuaFileContent content, CancellationToken cancellationToken)
            => innerStream.WriteAsync(content, cancellationToken);

        public ValueTask FlushAsync(CancellationToken cancellationToken)
            => innerStream.FlushAsync(cancellationToken);

        public void SetVBuf(LuaFileBufferingMode mode, int size)
            => innerStream.SetVBuf(mode, size);

        public long Seek(long offset, SeekOrigin origin)
            => innerStream.Seek(offset, origin);

        public void Close()
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