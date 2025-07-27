namespace Lua.CodeAnalysis.Compilation;

readonly ref struct TempBlock(LuaGlobalState globalState)
{
    public void Dispose()
    {
        globalState.CallCount--;
    }
}