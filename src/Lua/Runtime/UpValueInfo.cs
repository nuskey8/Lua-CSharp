namespace Lua.Runtime;

public readonly record struct UpValueInfo
{
    public required ReadOnlyMemory<char> Name { get; init; }
    public required int Index { get; init; }
    public required int Id { get; init; }
    public required bool IsInRegister { get; init; }
}
