namespace Lua.CodeAnalysis;

public record struct LocalVariable
{
    public string Name;
    public int StartPc, EndPc;
}