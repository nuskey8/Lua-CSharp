using System.Buffers;
using System.Runtime.CompilerServices;

namespace Lua.Internal;

public struct PooledArray<T>(int sizeHint) : IDisposable
{
    T[]? array = ArrayPool<T>.Shared.Rent(sizeHint);

    public ref T this[int index]
    {
        get
        {
            ThrowIfDisposed();
            return ref array![index];
        }
    }
    public T[] UnderlyingArray
    {
        get
        {
            ThrowIfDisposed();
            return array!;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> AsSpan()
    {
        ThrowIfDisposed();
        return array;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Memory<T> AsMemory()
    {
        ThrowIfDisposed();
        return array;
    }

    public void Dispose()
    {
        ThrowIfDisposed();
        ArrayPool<T>.Shared.Return(array!);
        array = null;
    }

    void ThrowIfDisposed()
    {
        if (array == null)
        {
            throw new ObjectDisposedException(nameof(PooledArray<T>));
        }
    }
}