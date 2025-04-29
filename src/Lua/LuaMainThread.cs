namespace Lua;

public sealed class LuaMainThread : LuaThread
{
    internal LuaMainThread(LuaState state)
    {
        State = state;
        CoreData = ThreadCoreData.Create();
    }

    public override LuaThreadStatus GetStatus()
    {
        return LuaThreadStatus.Running;
    }
}