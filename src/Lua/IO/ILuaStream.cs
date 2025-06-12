namespace Lua.IO
{
    public interface ILuaStream : IDisposable
    {
        public LuaFileOpenMode Mode { get; }

        public ValueTask<string> ReadAllAsync(CancellationToken cancellationToken)
        {
            Mode.ThrowIfNotReadable();

            // Default implementation using ReadStringAsync
            throw new NotImplementedException($"ReadAllAsync must be implemented by {GetType().Name}");
        }

        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            Mode.ThrowIfNotReadable();


            // Default implementation using ReadStringAsync
            throw new NotImplementedException($"ReadLineAsync must be implemented by {GetType().Name}");
        }

        public ValueTask<string?> ReadStringAsync(int count, CancellationToken cancellationToken)
        {
            Mode.ThrowIfNotReadable();

            // Default implementation using ReadAllAsync
            throw new NotImplementedException($"ReadStringAsync must be implemented by {GetType().Name}");
        }

        public ValueTask WriteAsync(ReadOnlyMemory<char> content, CancellationToken cancellationToken)
        {
            Mode.ThrowIfNotWritable();

            throw new NotImplementedException($"WriteAsync must be implemented by {GetType().Name}");
        }

        public ValueTask WriteAsync(string content, CancellationToken cancellationToken) => WriteAsync(content.AsMemory(), cancellationToken);

        public ValueTask FlushAsync(CancellationToken cancellationToken)
        {
            // Default implementation does nothing (no buffering)
            return default;
        }

        public void SetVBuf(LuaFileBufferingMode mode, int size)
        {
            // Default implementation does nothing (no configurable buffering)
        }

        public long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException($"Seek is not supported by {GetType().Name}");
        }

        public static ILuaStream CreateStreamWrapper(Stream stream, LuaFileOpenMode openMode)
        {
            return new TextLuaStream(openMode, stream);
        }

        public static ILuaStream CreateFromFileString(string content)
        {
            return new StringStream(content);
        }

        public static ILuaStream CreateFromMemory(ReadOnlyMemory<char> content)
        {
            return new CharMemoryStream(content);
        }


        public void Close()
        {
            Dispose();
        }
    }
}