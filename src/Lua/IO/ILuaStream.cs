namespace Lua.IO
{
    public interface ILuaStream : IDisposable
    {
        public LuaFileMode Mode { get; }

        public ValueTask<LuaFileContent> ReadAllAsync(CancellationToken cancellationToken)
        {
            Mode.ThrowIfNotReadable();

            // Default implementation using ReadStringAsync
            throw new NotImplementedException($"ReadAllAsync must be implemented by {GetType().Name}");
        }

        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            Mode.ThrowIfNotReadable();

            Mode.ThrowIfNotText();

            // Default implementation using ReadStringAsync
            throw new NotImplementedException($"ReadLineAsync must be implemented by {GetType().Name}");
        }

        public ValueTask<string?> ReadStringAsync(int count, CancellationToken cancellationToken)
        {
            Mode.ThrowIfNotReadable();

            Mode.ThrowIfNotText();
            // Default implementation using ReadAllAsync
            throw new NotImplementedException($"ReadStringAsync must be implemented by {GetType().Name}");
        }

        public ValueTask WriteAsync(LuaFileContent content, CancellationToken cancellationToken)
        {
            Mode.ThrowIfNotWritable();
            if (content.Type == LuaFileContentType.Binary)
            {
                Mode.ThrowIfNotBinary();
            }
            else
            {
                Mode.ThrowIfNotText();
            }

            throw new NotImplementedException($"WriteAsync must be implemented by {GetType().Name}");
        }

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

        public static ILuaStream CreateStreamWrapper(Stream stream, LuaFileOpenMode openMode, LuaFileContentType contentType = LuaFileContentType.Text)
        {
            var mode = LuaFileModeExtensions.GetMode(openMode, contentType);
            return contentType == LuaFileContentType.Binary
                ? new BinaryLuaStream(mode, stream)
                : new TextLuaStream(mode, stream);
        }
        
        public static ILuaStream CreateFromFileContent(LuaFileContent content)
        {
            if (content.Type == LuaFileContentType.Binary)
            {
                return new ByteMemoryStream(content.ReadBytes() );
            }
            else
            {
                return new StringStream(content.ReadString());
            }
        }


        public void Close()
        {
            Dispose();
        }
    }
}