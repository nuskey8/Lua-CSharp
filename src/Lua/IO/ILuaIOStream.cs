namespace Lua.IO
{
    public interface ILuaIOStream : IDisposable
    {
        public LuaFileOpenMode Mode { get; }

        public LuaFileContentType ContentType { get; }
        public ValueTask<LuaFileContent> ReadAllAsync(CancellationToken cancellationToken);
        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken);
        public ValueTask<string?> ReadStringAsync(int count, CancellationToken cancellationToken);
        public ValueTask WriteAsync(LuaFileContent content, CancellationToken cancellationToken);
        public ValueTask FlushAsync(CancellationToken cancellationToken);
        public void SetVBuf(LuaFileBufferingMode mode, int size);
        public long Seek(long offset, SeekOrigin origin);

        public static ILuaIOStream CreateStreamWrapper(Stream stream, LuaFileOpenMode mode, LuaFileContentType contentType = LuaFileContentType.Text)
        {
            return contentType == LuaFileContentType.Binary
                ? new BinaryLuaIOStream(mode, stream)
                : new TextLuaIOStream(mode, stream);
        }

        public void Close()
        {
            Dispose();
        }
    }
}