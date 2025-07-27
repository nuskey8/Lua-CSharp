using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lua.Internal;

static class MemoryMarshalEx
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref T UnsafeElementAt<T>(T[] array, int index)
    {
#if NET6_0_OR_GREATER
        return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), index);
#else
        ref var reference = ref MemoryMarshal.GetReference(array.AsSpan());
        return ref Unsafe.Add(ref reference, index);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref T UnsafeElementAt<T>(Span<T> array, int index)
    {
        return ref Unsafe.Add(ref MemoryMarshal.GetReference(array), index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref T UnsafeElementAt<T>(ReadOnlySpan<T> array, int index)
    {
        return ref Unsafe.Add(ref MemoryMarshal.GetReference(array), index);
    }
}