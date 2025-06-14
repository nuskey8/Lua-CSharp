namespace Lua.IO
{
    public interface ILuaStream : IDisposable
    {
        public bool IsOpen { get; }
        public LuaFileOpenMode Mode { get; }

        public ValueTask<string> ReadAllAsync(CancellationToken cancellationToken)
        {
            Mode.ThrowIfNotReadable();

            // Default implementation using ReadStringAsync
            throw new NotImplementedException($"ReadAllAsync must be implemented by {GetType().Name}");
        }

        public ValueTask<double?> ReadNumberAsync(CancellationToken cancellationToken)
        {
            Mode.ThrowIfNotReadable();

            // Default implementation using ReadStringAsync
            throw new NotImplementedException($"ReadNumberAsync must be implemented by {GetType().Name}");
        }

        public ValueTask<string?> ReadLineAsync(bool keepEol, CancellationToken cancellationToken)
        {
            Mode.ThrowIfNotReadable();


            // Default implementation using ReadStringAsync
            throw new NotImplementedException($"ReadLineAsync must be implemented by {GetType().Name}");
        }

        public ValueTask<string?> ReadAsync(int count, CancellationToken cancellationToken)
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

        public long Seek(SeekOrigin origin, long offset)
        {
            throw new NotSupportedException($"Seek is not supported by {GetType().Name}");
        }

        public static ILuaStream CreateFromStream(Stream stream, LuaFileOpenMode openMode)
        {
            return new LuaStream(openMode, stream);
        }

        public static ILuaStream CreateFromString(string content)
        {
            return new StringStream(content);
        }

        public static ILuaStream CreateFromMemory(ReadOnlyMemory<char> content)
        {
            return new CharMemoryStream(content);
        }


        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            Dispose();
            return default;
        }
    }
}