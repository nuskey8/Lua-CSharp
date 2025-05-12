using Lua.Runtime;

namespace Lua;

public static class LuaThreadExtensions
{
    public static UseThreadLease RentUseThread(this LuaThread thread)
    {
        return new(LuaUserThread.Create(thread));
    }

    public static CoroutineLease RentCoroutine(this LuaThread thread, LuaFunction function, bool isProtectedMode = false)
    {
        return new(LuaCoroutine.Create(thread, function, isProtectedMode));
    }
}