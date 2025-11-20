using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lua.Internal;

interface IPoolNode<T>
{
    ref T? NextNode { get; }
}

// mutable struct, don't mark readonly.
[StructLayout(LayoutKind.Auto)]
struct LinkedPool<T>
    where T : class, IPoolNode<T>
{
    int gate;
    int size;
    T? root;

    public int Size => size;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPop(out T result)
    {
        if (Interlocked.CompareExchange(ref gate, 1, 0) == 0)
        {
            var v = root;
            if (!(v is null))
            {
                ref var nextNode = ref v.NextNode;
                root = nextNode;
                nextNode = null;
                size--;
                result = v;
                Volatile.Write(ref gate, 0);
                return true;
            }

            Volatile.Write(ref gate, 0);
        }

        result = default!;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPush(T item)
    {
        if (Interlocked.CompareExchange(ref gate, 1, 0) == 0)
        {
            if (size < 64)
            {
                item.NextNode = root;
                root = item;
                size++;
                Volatile.Write(ref gate, 0);
                return true;
            }
            else
            {
                Volatile.Write(ref gate, 0);
            }
        }

        return false;
    }
}