using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lua.Internal;

internal sealed class Utf16StringMemoryComparer : IEqualityComparer<ReadOnlyMemory<char>>
{
    public static readonly Utf16StringMemoryComparer Default = new();

    public bool Equals(ReadOnlyMemory<char> x, ReadOnlyMemory<char> y)
    {
        return x.Span.SequenceEqual(y.Span);
    }

    public int GetHashCode(ReadOnlyMemory<char> obj)
    {
        var span = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<char, byte>(ref MemoryMarshal.GetReference(obj.Span)), obj.Length * 2);
        return (int)unchecked(FarmHash.Hash64(span));
    }
}