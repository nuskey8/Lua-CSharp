using Lua.Debugging;

sealed record HostOptions(
    string Scripts,
    string Entry,
    LuaDebugPauseMode PauseMode,
    bool Game,
    bool WaitForDebugger,
    int Port,
    int Frames
)
{
    public static HostOptions Parse(string[] args)
    {
        var scripts = Path.Combine(AppContext.BaseDirectory, "UserScripts");
        string? entry = null;
        var mode = LuaDebugPauseMode.Asynchronous;
        var game = true;
        var wait = false;
        var port = 4711;
        var frames = 0;
        for (var i = 0; i < args.Length; i++)
        {
            string Value() =>
                ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.");
            switch (args[i])
            {
                case "--scripts":
                    scripts = Value();
                    break;
                case "--entry":
                    entry = Value();
                    break;
                case "--pause":
                    mode = Value() switch
                    {
                        "async" => LuaDebugPauseMode.Asynchronous,
                        "sync" => LuaDebugPauseMode.Synchronous,
                        _ => throw new ArgumentException("Use --pause async or sync."),
                    };
                    break;
                case "--host":
                    game = Value() switch
                    {
                        "game" => true,
                        "service" => false,
                        _ => throw new ArgumentException("Use --host game or service."),
                    };
                    break;
                case "--port":
                    port = int.Parse(Value());
                    break;
                case "--frames":
                    frames = int.Parse(Value());
                    break;
                case "--wait-for-debugger":
                    wait = true;
                    break;
                default:
                    throw new ArgumentException("Unknown option: " + args[i]);
            }
        }
        if (frames < 0)
            throw new ArgumentOutOfRangeException(nameof(frames));
        return new(
            Path.GetFullPath(scripts),
            string.IsNullOrEmpty(entry) ? (game ? "main.lua" : "worker.lua") : entry,
            mode,
            game,
            wait,
            port,
            frames
        );
    }
}
