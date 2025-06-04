using Lua.CodeAnalysis.Compilation;
using System.Buffers;
using System.Text;

namespace Lua.IO
{
    public sealed class LuaChunkStream : ILuaIOStream
    {
        public LuaChunkStream(Stream stream)
        {
            using (stream)
            {
                var length = stream.Length;
                if (length > int.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(stream), "Stream length exceeds maximum size for Lua chunk.");

                bytesToReturnToPool = ArrayPool<byte>.Shared.Rent((int)length);
                try
                {
                    var count = stream.Read(bytesToReturnToPool.AsSpan());
                    bytes = new ReadOnlyMemory<byte>(bytesToReturnToPool, 0, count);
                }
                catch (Exception)
                {
                    ArrayPool<byte>.Shared.Return(bytesToReturnToPool);
                }
            }
        }

        byte[]? bytesToReturnToPool;
        char[]? charsToReturnToPool;
        private readonly ReadOnlyMemory<byte> bytes;

        public void Dispose()
        {
            if (bytesToReturnToPool is not null)
            {
                ArrayPool<byte>.Shared.Return(bytesToReturnToPool);
            }
            else if (charsToReturnToPool is not null)
            {
                ArrayPool<char>.Shared.Return(charsToReturnToPool);
            }
        }

        public LuaFileOpenMode Mode => LuaFileOpenMode.Read;
        public LuaFileContentType ContentType => LuaFileContentType.Unknown;

        public ValueTask<LuaFileContent> ReadAllAsync(CancellationToken cancellationToken)
        {
            var span = bytes.Span;
            if (span.StartsWith(LuaCompiler.LuaByteCodeSignature))
            {
                var array = ArrayPool<byte>.Shared.Rent(span.Length);
                bytesToReturnToPool = array;
                return new ValueTask<LuaFileContent>(new LuaFileContent(array.AsMemory(span.Length)));
            }
            else
            {
                var encoding = Encoding.UTF8;
                var array = ArrayPool<char>.Shared.Rent(encoding.GetMaxCharCount(span.Length));
                var charCount = encoding.GetChars(span, array);
                charsToReturnToPool = array;
                return new ValueTask<LuaFileContent>(new LuaFileContent(array.AsMemory(0,charCount)));
            }
        }

        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public ValueTask<string?> ReadStringAsync(int count, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public ValueTask WriteAsync(LuaFileContent content, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public void SetVBuf(LuaFileBufferingMode mode, int size)
        {
            throw new NotSupportedException();
        }

        public long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }
    }
}