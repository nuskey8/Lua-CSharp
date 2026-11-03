namespace Lua.Debugging;

public enum LuaDebugPauseMode
{
    /// <summary>Suspends Lua execution without blocking its host thread.</summary>
    Asynchronous,

    /// <summary>Blocks the execution thread, servicing only debugger work until resumed.</summary>
    Synchronous,
}

public sealed class LuaDebuggerOptions
{
    public LuaDebugPauseMode PauseMode { get; init; }

    /// <summary>Maps a decoded Lua chunk name to its editor file path, or null for the default mapping.</summary>
    public Func<string, string?>? SourcePathResolver { get; init; }

    /// <summary>Reads application-owned source text. Null falls back to the state's filesystem.</summary>
    public Func<
        LuaDebugSource,
        CancellationToken,
        ValueTask<string?>
    >? SourceTextProvider { get; init; }
}
