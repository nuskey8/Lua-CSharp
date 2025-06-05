using Lua.IO;

namespace Lua.Tests.Helpers
{
    public class NotSupportedStreamBase : ILuaStream
    {
        public virtual void Dispose()
        {
        }

        public virtual LuaFileMode Mode => throw IOThrowHelpers.GetNotSupportedException();
        public LuaFileContentType ContentType=> throw IOThrowHelpers.GetNotSupportedException();

        public virtual ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            throw IOThrowHelpers.GetNotSupportedException();
        }

        public virtual ValueTask<LuaFileContent> ReadAllAsync(CancellationToken cancellationToken)
        {
            throw IOThrowHelpers.GetNotSupportedException();
        }

        public virtual ValueTask<string?> ReadStringAsync(int count, CancellationToken cancellationToken)
        {
            throw IOThrowHelpers.GetNotSupportedException();
        }

        public virtual ValueTask WriteAsync(LuaFileContent content, CancellationToken cancellationToken)
        {
            throw IOThrowHelpers.GetNotSupportedException();
        }

        public virtual ValueTask FlushAsync(CancellationToken cancellationToken)
        {
            throw IOThrowHelpers.GetNotSupportedException();
        }

        public virtual void SetVBuf(LuaFileBufferingMode mode, int size)
        {
            throw IOThrowHelpers.GetNotSupportedException();
        }

        public virtual long Seek(long offset, SeekOrigin origin)
        {
            throw IOThrowHelpers.GetNotSupportedException();
        }
    }
}