using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lua.Internal;

/// <summary>
/// A list of minimal features. Note that it is NOT thread-safe and must NOT be marked readonly as it is a mutable struct.
/// </summary>
/// <typeparam name="T">Element type</typeparam>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("Count = {Length}")]
public struct FastListCore<T>
{
    const int InitialCapacity = 8;

    public static readonly FastListCore<T> Empty = default;

    T[]? array;
    int tailIndex;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T element)
    {
        if (array == null)
        {
            array = new T[InitialCapacity];
        }
        else if (array.Length == tailIndex)
        {
            Array.Resize(ref array, tailIndex * 2);
        }

        array[tailIndex] = element;
        tailIndex++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Pop()
    {
        CheckIndex(tailIndex - 1);
        array![tailIndex - 1] = default!;
        tailIndex--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RemoveAtSwapBack(int index)
    {
        CheckIndex(index);

        array![index] = array[tailIndex - 1];
        array[tailIndex - 1] = default!;
        tailIndex--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Shrink(int newSize)
    {
        if (newSize >= tailIndex)
        {
            return;
        }

        array.AsSpan(newSize).Clear();
        tailIndex = newSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear(bool removeArray = false)
    {
        if (array == null)
        {
            return;
        }

        array.AsSpan().Clear();
        tailIndex = 0;
        if (removeArray)
        {
            array = null;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnsureCapacity(int capacity)
    {
        if (array == null)
        {
            array = new T[InitialCapacity];
        }

        while (array.Length < capacity)
        {
            Array.Resize(ref array, array.Length * 2);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CopyTo(ref FastListCore<T> destination)
    {
        destination.EnsureCapacity(tailIndex);
        destination.tailIndex = tailIndex;
        AsSpan().CopyTo(destination.AsSpan());
    }

    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref array![index];
    }

    public readonly int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => tailIndex;
    }

    public readonly Span<T> AsSpan()
    {
        return array == null ? Span<T>.Empty : array.AsSpan(0, tailIndex);
    }

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    readonly Span<T> Span => AsSpan();

    public readonly T[]? AsArray()
    {
        return array;
    }

    readonly void CheckIndex(int index)
    {
        if (array == null || index < 0 || index > tailIndex)
        {
            ThrowIndexOutOfRange();
        }
    }

    static void ThrowIndexOutOfRange()
    {
        throw new IndexOutOfRangeException();
    }
}