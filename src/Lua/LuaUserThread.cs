using Lua.Internal;

namespace Lua;

public sealed class LuaUserThread : LuaThread, IPoolNode<LuaUserThread>
{
    static LinkedPool<LuaUserThread> pool;
    LuaUserThread? nextNode;
    ref LuaUserThread? IPoolNode<LuaUserThread>.NextNode => ref nextNode;

    public static LuaUserThread Create(LuaThread parent)
    {
        if (!pool.TryPop(out LuaUserThread result))
        {
            result = new LuaUserThread();
        }

        result.State = parent.State;
        result.CoreData = ThreadCoreData.Create();

        return result;
    }

    public void Release()
    {
        if (CoreData!.CallStack.Count != 0)
        {
            throw new InvalidOperationException("This thread is running! Call stack is not empty!!");
        }

        CoreData.Release();
        CoreData = null!;
        pool.TryPush(this);
    }
}