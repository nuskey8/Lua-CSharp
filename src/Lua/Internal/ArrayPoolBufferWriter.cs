using System.Buffers;

namespace Lua.Internal;

sealed class ArrayPoolBufferWriter<T>(int initialCapacity = 256) : IBufferWriter<T>, IDisposable
{
    T[] buffer = initialCapacity > 0 ? ArrayPool<T>.Shared.Rent(initialCapacity) : throw new ArgumentOutOfRangeException(nameof(initialCapacity));
    int index;

    public ReadOnlySpan<T> WrittenSpan => buffer.AsSpan(0, index);

    public void Advance(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (index > buffer.Length - count)
        {
            throw new InvalidOperationException("Cannot advance past the end of the buffer.");
        }

        index += count;
    }

    public Memory<T> GetMemory(int sizeHint = 0)
    {
        CheckAndResizeBuffer(sizeHint);
        return buffer.AsMemory(index);
    }

    public Span<T> GetSpan(int sizeHint = 0)
    {
        CheckAndResizeBuffer(sizeHint);
        return buffer.AsSpan(index);
    }

    void CheckAndResizeBuffer(int sizeHint)
    {
        if (sizeHint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeHint));
        }

        if (sizeHint == 0)
        {
            sizeHint = 1;
        }

        if (sizeHint <= buffer.Length - index)
        {
            return;
        }

        var newSize = Math.Max(buffer.Length * 2, index + sizeHint);
        var newBuffer = ArrayPool<T>.Shared.Rent(newSize);
        buffer.AsSpan(0, index).CopyTo(newBuffer);
        ArrayPool<T>.Shared.Return(buffer);
        buffer = newBuffer;
    }

    public void Dispose()
    {
        ArrayPool<T>.Shared.Return(buffer);
        buffer = [];
        index = 0;
    }
}
