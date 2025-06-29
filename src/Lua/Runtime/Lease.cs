namespace Lua.Runtime
{
    public readonly struct UseThreadLease(LuaUserThread thread) : IDisposable
    {
        public LuaUserThread Thread { get; } = thread;

        public void Dispose()
        {
            Thread.Release();
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
}