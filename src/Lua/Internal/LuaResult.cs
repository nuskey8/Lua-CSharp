using Lua.Runtime;

namespace Lua.Internal;

public readonly struct LuaResult : IDisposable
{
    readonly LuaStack stack;
    readonly int returnBase;

    internal LuaResult(LuaStack stack, int returnBase)
    {
        this.stack = stack;
        this.returnBase = returnBase;
    }

    public int Count => stack.Count - returnBase;
    public int Length => stack.Count - returnBase;
    public ReadOnlySpan<LuaValue> AsSpan() => stack.AsSpan()[returnBase..];

    public LuaValue this[int index] => AsSpan()[index];

    public void Dispose()
    {
        stack.PopUntil(returnBase);
    }
}