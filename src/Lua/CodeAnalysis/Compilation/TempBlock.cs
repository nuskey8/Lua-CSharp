namespace Lua.CodeAnalysis.Compilation;

readonly ref struct TempBlock(LuaState state)
{
    public void Dispose()
    {
        state.CallCount--;
    }
}