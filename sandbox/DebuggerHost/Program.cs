using Lua;
using Lua.Debugger.Dap;
using Lua.Debugging;
using Lua.Standard;

if (args.Contains("--help"))
{
    Console.WriteLine(
        "DebuggerHost [--scripts DIR] [--entry FILE] [--host game|service] [--pause async|sync] [--port PORT] [--wait-for-debugger] [--frames COUNT]"
    );
    return;
}
var options = HostOptions.Parse(args);
if (options.Game)
    HostLoop.Run(() => RunAsync(options));
else
    await RunAsync(options);

static async Task RunAsync(HostOptions options)
{
    using var shutdown = new CancellationTokenSource();
    ConsoleCancelEventHandler cancel = (_, e) =>
    {
        e.Cancel = true;
        shutdown.Cancel();
    };
    Console.CancelKeyPress += cancel;
    var token = shutdown.Token;
    var scripts = new UserScriptStore(options.Scripts);
    using var state = LuaState.Create();
    state.OpenStandardLibraries();
    state.ModuleLoader = scripts;
    var frames = 0;
    var updates = 0;
    state.Environment["host"] = new LuaTable
    {
        ["log"] = new LuaFunction(
            (context, _) =>
            {
                Console.WriteLine("[script] " + context.GetArgument<string>(0));
                return new(context.Return());
            }
        ),
        ["frame"] = new LuaFunction((context, _) => new(context.Return(Volatile.Read(ref frames)))),
        ["read_text"] = new LuaFunction(
            async (context, cancellationToken) =>
            {
                var path = Path.Combine(options.Scripts, context.GetArgument<string>(0));
                var text = await File.ReadAllTextAsync(path, cancellationToken);
                return context.Return(text.TrimEnd());
            }
        ),
    };
    using var server = new LuaDapServer(
        state,
        new LuaDebuggerOptions
        {
            PauseMode = options.PauseMode,
            SourcePathResolver = scripts.ResolvePath,
            SourceTextProvider = scripts.ReadSourceAsync,
        },
        options.Port
    );
    server.Debugger.Stopped += stop =>
        Console.WriteLine($"[debug] {stop.Reason} at {stop.Source.Name}:{stop.Line}");
    Task listening = Task.CompletedTask;
    Task heartbeat = Task.CompletedTask;
    Task? invocation = null;
    try
    {
        listening = server.RunAsync(token);
        if (listening.IsCompleted)
            await listening;
        Console.Error.WriteLine(
            $"Lua debugger listening on {server.LocalEndpoint}. Host={(options.Game ? "game" : "service")}, pause={options.PauseMode}."
        );
        Console.WriteLine(
            $"User scripts: {options.Scripts}; entry: {options.Entry}. Ctrl+C stops the application."
        );
        heartbeat = HeartbeatAsync();
        if (options.WaitForDebugger)
            await server.WaitForConfigurationAsync(token);
        var closure = state.Load(await scripts.ReadAsync(options.Entry, token));
        var result = await state.ExecuteAsync(closure, token);
        var handler =
            result.Length == 1 && result[0].TryRead<LuaFunction>(out var function)
                ? function
                : throw new InvalidOperationException(
                    "The entry script must return update(frame, dt) for a game host or handle(job) for a service host."
                );
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (options.Frames == 0 || Volatile.Read(ref frames) < options.Frames)
        {
            await timer.WaitForNextTickAsync(token);
            var frame = Interlocked.Increment(ref frames);
            if (invocation is { IsCompleted: true })
            {
                var completed = invocation;
                invocation = null;
                await completed;
            }
            if (invocation == null)
                invocation = InvokeAsync(handler, frame);
        }
        if (invocation != null)
        {
            var completed = invocation;
            invocation = null;
            await completed;
        }
        await server.NotifyTerminatedAsync(token);
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception);
        Environment.ExitCode = 1;
    }
    finally
    {
        shutdown.Cancel();
        server.Dispose();
        try
        {
            var finishing =
                invocation == null
                    ? [listening, heartbeat]
                    : new[] { listening, heartbeat, invocation };
            await Task.WhenAll(finishing);
        }
        catch (LuaCanceledException) { }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            Environment.ExitCode = 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    async Task InvokeAsync(LuaFunction handler, int id)
    {
        if (options.Game)
            await state.CallAsync(handler, [id, 0.25], token);
        else
        {
            var job = new LuaTable
            {
                ["id"] = id,
                ["unit_price"] = 12.5,
                ["quantity"] = id % 3 + 1,
            };
            var results = await state.CallAsync(handler, [job], token);
            Console.WriteLine($"[service] job {id} returned {string.Join(", ", results)}");
        }
        Interlocked.Increment(ref updates);
    }

    async Task HeartbeatAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(token))
                Console.WriteLine(
                    $"[app] frame={Volatile.Read(ref frames)} updates={Volatile.Read(ref updates)} {(server.Debugger.CurrentStop == null ? "running" : "script paused")}"
                );
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
