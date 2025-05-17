namespace Lua.CodeAnalysis.Compilation;

internal readonly ref struct TempBlock(LuaState state)
{
    public void Dispose()
    {
        state.CallCount--;
    }
}