using Lua.CodeAnalysis.Compilation;
using System.Buffers;
using System.Text;

namespace Lua.IO;

public sealed class LuaChunkStream : ILuaStream
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
                bytes = bytesToReturnToPool.AsMemory(0, count);
            }
            catch (Exception)
            {
                ArrayPool<byte>.Shared.Return(bytesToReturnToPool);
            }
        }
    }

    public LuaChunkStream(ReadOnlyMemory<byte> bytes, IDisposable? disposable = null)
    {
        this.bytes = bytes;
        this.disposable = disposable;
    }

    byte[]? bytesToReturnToPool;
    char[]? charsToReturnToPool;
    private readonly ReadOnlyMemory<byte> bytes;
    private IDisposable? disposable;

    public void Dispose()
    {
        if (bytesToReturnToPool is not null)
        {
            ArrayPool<byte>.Shared.Return(bytesToReturnToPool);
            bytesToReturnToPool = null!;
        }

        if (charsToReturnToPool is not null)
        {
            ArrayPool<char>.Shared.Return(charsToReturnToPool);
            charsToReturnToPool = null!;
        }

        disposable?.Dispose();
        disposable = null;
    }

    public LuaFileMode Mode => LuaFileMode.Read | (bytes.Span.StartsWith(LuaCompiler.LuaByteCodeSignature) ? LuaFileMode.Binary : LuaFileMode.Text);

    public ValueTask<LuaFileContent> ReadAllAsync(CancellationToken cancellationToken)
    {
        var span = bytes.Span;
        if ((Mode & LuaFileMode.Binary) != 0)
        {
            return new(new LuaFileContent(bytes));
        }
        else
        {
            var encoding = Encoding.UTF8;
            var array = ArrayPool<char>.Shared.Rent(encoding.GetMaxCharCount(span.Length));
            var charCount = encoding.GetChars(span, array);
            charsToReturnToPool = array;
            return new(new LuaFileContent(array.AsMemory(0, charCount)));
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