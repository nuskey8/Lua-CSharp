namespace Lua.CodeAnalysis;

public record struct UpValueDesc
{
    public string Name;
    public bool IsLocal;
    public int Index;
}