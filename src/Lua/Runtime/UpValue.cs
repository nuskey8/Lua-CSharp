using System.Runtime.CompilerServices;

namespace Lua.Runtime;

public sealed class UpValue
{
    LuaValue value;

    // A running thread, a weak reference to a suspended thread, or null after closing.
    object? owner;

    public LuaState? Thread =>
        owner is LuaState thread ? thread
        : owner is WeakReference<LuaState> reference && reference.TryGetTarget(out var suspended)
            ? suspended
        : null;
    public bool IsClosed =>
        owner is null
        || owner is WeakReference<LuaState> reference && !reference.TryGetTarget(out _);
    public int RegisterIndex { get; private set; }

    UpValue() { }

    public static UpValue Open(LuaState state, int registerIndex)
    {
        return new() { owner = state, RegisterIndex = registerIndex };
    }

    public static UpValue Closed(LuaValue value)
    {
        return new() { value = value };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuaValue GetValue()
    {
        if (owner is LuaState thread)
        {
            return thread.Stack.Get(RegisterIndex);
        }
        else
        {
            return value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref LuaValue GetValueRef()
    {
        if (owner is LuaState thread)
        {
            return ref thread.Stack.Get(RegisterIndex);
        }
        else
        {
            return ref value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetValue(LuaValue value)
    {
        if (owner is LuaState thread)
        {
            thread.Stack.Get(RegisterIndex) = value;
        }
        else
        {
            this.value = value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Close()
    {
        if (owner is LuaState thread)
        {
            value = thread.Stack.Get(RegisterIndex);
        }
        else if (
            owner is WeakReference<LuaState> reference
            && reference.TryGetTarget(out var suspended)
        )
        {
            // Explicitly closing a suspended capture leaves its former local intact.
            suspended.Stack.Get(RegisterIndex) = value;
        }
        owner = null;
    }

    internal void Suspend(WeakReference<LuaState> reference)
    {
        if (owner is LuaState thread)
        {
            ref var register = ref thread.Stack.Get(RegisterIndex);
            value = register;
            register = default;
            owner = reference;
        }
    }

    internal bool Resume(LuaState thread)
    {
        if (owner is not WeakReference<LuaState>)
            return false;

        thread.Stack.Get(RegisterIndex) = value;
        value = default;
        owner = thread;
        return true;
    }
}
