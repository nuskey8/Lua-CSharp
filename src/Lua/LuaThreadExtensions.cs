using Lua.Runtime;

namespace Lua;

public static class LuaThreadExtensions
{
    public static UserThreadLease RentUserThread(this LuaThread thread)
    {
        return new(LuaUserThread.Create(thread));
    }

    public static CoroutineLease RentCoroutine(this LuaThread thread, LuaFunction function, bool isProtectedMode = false)
    {
        return new(LuaCoroutine.Create(thread, function, isProtectedMode));
    }

    internal static void ThrowIfCancellationRequested(this LuaThread thread, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            Throw(thread, cancellationToken);
        }

        return;

        static void Throw(LuaThread thread, CancellationToken cancellationToken)
        {
            throw new LuaCanceledException(thread, cancellationToken);
        }
    }
}