namespace Lua.Debugging;

public enum LuaDebugStep
{
    Continue,
    Into,
    Over,
    Out,
}

public enum LuaDebugScope
{
    Locals,
    UpValues,
    Globals,
}

public sealed record LuaDebugSource(int Id, string Name, string? Path, string? Content);

public sealed record LuaBreakpoint(int Id, int Line, bool Verified, string? Message = null);

public sealed record LuaDebugStop(
    int Id,
    string Reason,
    LuaDebugSource Source,
    int Line,
    int? BreakpointId
);

public sealed record LuaDebugStackFrame(int Id, string Name, LuaDebugSource? Source, int Line);

public sealed record LuaDebugVariable(string Name, LuaValue Value, string DisplayValue)
{
    public bool IsReadOnly { get; init; }
}
