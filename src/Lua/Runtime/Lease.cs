namespace Lua.Runtime;

public readonly struct UserThreadLease(LuaUserThread thread) : IDisposable
{
    public LuaUserThread State { get; } = thread;

    public void Dispose()
    {
        State.Release();
    }
}

public readonly struct CoroutineLease(LuaCoroutine thread) : IDisposable
{
    public LuaCoroutine Thread { get; } = thread;

    public void Dispose()
    {
        Thread.Release();
    }
}