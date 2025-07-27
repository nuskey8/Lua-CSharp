using Lua.Internal;

namespace Lua;

public sealed class LuaUserThread : LuaState, IPoolNode<LuaUserThread>
{
    static LinkedPool<LuaUserThread> pool;
    LuaUserThread? nextNode;

    ref LuaUserThread? IPoolNode<LuaUserThread>.NextNode => ref nextNode;

    public static LuaUserThread Create(LuaState parent)
    {
        if (!pool.TryPop(out var result))
        {
            result = new();
        }

        result.GlobalState = parent.GlobalState;
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