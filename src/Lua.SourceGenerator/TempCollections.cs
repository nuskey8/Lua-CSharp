namespace Lua.SourceGenerator;

class TempCollections
{
    public readonly HashSet<LuaObjectMetamethod> Metamethods = new();
    public readonly List<string> InvalidMemberNames = new();
    public void Clear()
    {
        Metamethods.Clear();
        InvalidMemberNames.Clear();
    }
}