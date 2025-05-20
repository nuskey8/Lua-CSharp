using Lua.IO;

namespace Lua.Tests.Helpers
{
    public class NotSupportedStreamBase : ILuaIOStream
    {
        public virtual void Dispose()
        {
        }

        public virtual ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            throw IOThrowHelpers.GetNotSupportedException();
        }

        public virtual ValueTask<string> ReadToEndAsync(CancellationToken cancellationToken)
        {
            throw IOThrowHelpers.GetNotSupportedException();
        }

        public virtual ValueTask<string?> ReadStringAsync(int count, CancellationToken cancellationToken)
        {
            throw IOThrowHelpers.GetNotSupportedException();
        }

        public virtual ValueTask WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken)
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

        public virtual  long Seek(long offset, SeekOrigin origin)
        {
            throw IOThrowHelpers.GetNotSupportedException();
        }
    }
}