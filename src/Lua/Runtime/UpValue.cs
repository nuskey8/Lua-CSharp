using System.Runtime.CompilerServices;

namespace Lua.Runtime;

public sealed class UpValue
{
    LuaValue value;

    public LuaThread? Thread { get; }
    public bool IsClosed { get; private set; }
    public int RegisterIndex { get; private set; }

    UpValue(LuaThread? thread)
    {
        Thread = thread;
    }

    public static UpValue Open(LuaThread thread, int registerIndex)
    {
        return new(thread) { RegisterIndex = registerIndex };
    }

    public static UpValue Closed(LuaValue value)
    {
        return new(null) { IsClosed = true, value = value };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuaValue GetValue()
    {
        if (IsClosed)
        {
            return value;
        }
        else
        {
            return Thread!.Stack.Get(RegisterIndex);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref readonly LuaValue GetValueRef()
    {
        if (IsClosed)
        {
            return ref value;
        }
        else
        {
            return ref Thread!.Stack.Get(RegisterIndex);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetValue(LuaValue value)
    {
        if (IsClosed)
        {
            this.value = value;
        }
        else
        {
            Thread!.Stack.Get(RegisterIndex) = value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Close()
    {
        if (!IsClosed)
        {
            value = Thread!.Stack.Get(RegisterIndex);
        }

        IsClosed = true;
    }
}