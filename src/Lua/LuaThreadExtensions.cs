using Lua.Runtime;

namespace Lua;

public static class LuaThreadExtensions
{
    public static UserThreadLease RentUserThread(this LuaState thread)
    {
        return new(LuaUserThread.Create(thread));
    }

    public static CoroutineLease RentCoroutine(this LuaState thread, LuaFunction function, bool isProtectedMode = false)
    {
        return new(LuaCoroutine.Create(thread, function, isProtectedMode));
    }

    internal static void ThrowIfCancellationRequested(this LuaState thread, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            Throw(thread, cancellationToken);
        }

        return;

        static void Throw(LuaState thread, CancellationToken cancellationToken)
        {
            throw new LuaCanceledException(thread, cancellationToken);
        }
    }
}