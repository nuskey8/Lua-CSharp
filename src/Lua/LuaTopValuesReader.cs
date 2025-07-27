using Lua.Runtime;

namespace Lua;

public readonly struct LuaTopValuesReader : IDisposable
{
    readonly LuaStack stack;
    readonly int returnBase;

    internal LuaTopValuesReader(LuaStack stack, int returnBase)
    {
        this.stack = stack;
        this.returnBase = returnBase;
    }

    public int Count => stack.Count - returnBase;

    public int Length => stack.Count - returnBase;

    public ReadOnlySpan<LuaValue> AsSpan()
    {
        return stack.AsSpan()[returnBase..];
    }

    public LuaValue this[int index] => AsSpan()[index];

    public void Dispose()
    {
        stack.PopUntil(returnBase);
    }
}