using Lua.Internal;
using Lua.Runtime;

namespace Lua;

public static class LuaThreadExtensions
{
    public static async ValueTask<LuaResult> RunAsync(this LuaThread thread, LuaClosure closure, int argumentCount, CancellationToken cancellationToken = default)
    {
        var top = thread.CoreData.Stack.Count;
        await closure.InvokeAsync(new()
        {
            Thread = thread, ArgumentCount = argumentCount, ReturnFrameBase = top - argumentCount, SourceLine = null,
        }, cancellationToken);

        return new LuaResult(thread.Stack, top);
    }
    
    
    public static UseThreadLease RentUseThread(this LuaThread thread)
    {
        return new UseThreadLease(LuaUserThread.Create(thread));
    }

    public static CoroutineLease RentCoroutine(this LuaThread thread,LuaFunction function ,bool isProtectedMode = false)
    {
        return new CoroutineLease(LuaCoroutine.Create(thread, function, isProtectedMode));
    }
}