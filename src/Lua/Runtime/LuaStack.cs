using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lua.Internal;
using System.Diagnostics;

namespace Lua.Runtime;

public sealed class LuaStack(int initialSize = 256)
{
    LuaValue[] array = new LuaValue[initialSize];
    int top;

    public int Count => top;

    public ref LuaValue this[int index] => ref Get(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnsureCapacity(int newSize)
    {
        if (array.Length >= newSize) return;

        Resize(ref array, newSize);
        return;

        static void Resize(ref LuaValue[] array, int newSize)
        {
            var size = array.Length;
            while (size < newSize)
            {
                size *= 2;
            }

            if (1000000 < size)
            {
                throw new ("Lua Stack overflow");
            }

            Array.Resize(ref array, size);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void NotifyTop(int top)
    {
        if (this.top < top) this.top = top;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Push(LuaValue value)
    {
        EnsureCapacity(top + 1);
        array[top] = value;
        top++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushRange(params ReadOnlySpan<LuaValue> values)
    {
        EnsureCapacity(top + values.Length);
        values.CopyTo(array.AsSpan()[top..]);
        top += values.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuaValue Pop()
    {
        if (top == 0) ThrowEmptyStack();
        top--;
        var item = array[top];
        array[top] = default;
        return item;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Pop(int count)
    {
        var newSize = top - count;
        if (newSize >= top) return;

        array.AsSpan(newSize, top - newSize).Clear();
        top = newSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PopUntil(int newSize)
    {
        if (newSize >= top) return;

        array.AsSpan(newSize, top - newSize).Clear();
        top = newSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        array.AsSpan().Clear();
        top = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<LuaValue> AsSpan()
    {
        return new Span<LuaValue>(array, 0, top);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<LuaValue> GetBuffer()
    {
        return array.AsSpan();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Memory<LuaValue> GetBufferMemory()
    {
        return array.AsMemory();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref LuaValue UnsafeGet(int index)
    {
        return ref MemoryMarshalEx.UnsafeElementAt(array, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref LuaValue Get(int index)
    {
        return ref array[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref LuaValue FastGet(int index)
    {
#if NET6_0_OR_GREATER
        return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), index);
#else
        return ref array[index];
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref LuaValue GetWithNotifyTop(int index)
    {
        if (this.top <= index) this.top = index + 1;
#if NET6_0_OR_GREATER
        return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), index);
#else
        return ref array[index];
#endif
    }

    static void ThrowEmptyStack()
    {
        throw new InvalidOperationException("Empty stack");
    }

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    private Span<LuaValue> Span => AsSpan();

    internal void SetTop(int top)
    {
        EnsureCapacity(top);
        NotifyTop(top);
        PopUntil(top);
    }
}