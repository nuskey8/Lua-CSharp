using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Lua.Debugging;
using Lua.Standard;
using NUnit.Framework;

namespace Lua.Debugger.Dap.Tests;

public class DapTests
{
    [TestCase(LuaDebugPauseMode.Asynchronous)]
    [TestCase(LuaDebugPauseMode.Synchronous)]
    public async Task ServerDisposalCleansUpWhenInspectionCancellationThrows(LuaDebugPauseMode mode)
    {
        await using var host = await Host.CreateAsync(mode);
        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var cleanupRelease = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var drained = false;
        var ended = 0;
        host.Server.Debugger.StopEnded += _ => ended++;
        host.State.Environment["wait"] = new LuaFunction(
            async (context, token) =>
            {
                var waiting = new TaskCompletionSource<bool>().Task.WaitAsync(token);
                using var registration = token.Register(() =>
                    throw new InvalidOperationException("cancellation callback failed")
                );
                try
                {
                    entered.TrySetResult(true);
                    await waiting;
                    return context.Return();
                }
                finally
                {
                    await cleanupRelease.Task.WaitAsync(host.Token);
                    drained = true;
                }
            }
        );
        await host.InitializeAsync(stopOnEntry: true);
        await host.RequestAsync("configurationDone");
        var execution = host.Execute(host.State.Load("return 42", "=dispose"));
        await host.EventAsync("stopped");
        await host.SendAsync("evaluate", new() { ["frameId"] = 1, ["expression"] = "wait()" });
        await entered.Task.WaitAsync(host.Token);
        try
        {
            var failure = Assert.Throws<AggregateException>(host.Server.Dispose);
            Assert.That(failure!.ToString(), Does.Contain("cancellation callback failed"));
            Assert.That(host.Server.Debugger.IsAttached, Is.False);
            Assert.Throws<ObjectDisposedException>(host.Server.Debugger.Attach);
            Assert.DoesNotThrow(host.Server.Dispose);
            cleanupRelease.TrySetResult(true);
            Assert.That(await execution.WaitAsync(host.Token), Is.EqualTo(new LuaValue[] { 42 }));
            Assert.That(drained, Is.True);
            Assert.That(ended, Is.EqualTo(1));
        }
        finally
        {
            cleanupRelease.TrySetResult(true);
            host.Server.Debugger.Detach();
            await execution.WaitAsync(host.Token);
        }
    }

    [TestCase(LuaDebugPauseMode.Asynchronous)]
    [TestCase(LuaDebugPauseMode.Synchronous)]
    public async Task ClientSteppingDoesNotSendAnExtraContinuedEvent(LuaDebugPauseMode mode)
    {
        await using var host = await Host.CreateAsync(mode);
        await host.InitializeAsync(stopOnEntry: true);
        await host.RequestAsync("configurationDone");
        var execution = host.Execute(host.State.Load("local x=42\nreturn x", "=client-step"));
        await host.EventAsync("stopped");
        await host.RequestAsync("next", new() { ["threadId"] = 1 });
        await host.EventAsync("stopped");
        Assert.That(host.ReceivedEvents, Does.Not.Contain("continued"));
        await host.RequestAsync("continue", new() { ["threadId"] = 1 });
        Assert.That(await execution.WaitAsync(host.Token), Is.EqualTo(new LuaValue[] { 42 }));
    }

    [TestCase(LuaDebugPauseMode.Asynchronous, "continue")]
    [TestCase(LuaDebugPauseMode.Synchronous, "continue")]
    [TestCase(LuaDebugPauseMode.Asynchronous, "reset")]
    [TestCase(LuaDebugPauseMode.Synchronous, "reset")]
    [TestCase(LuaDebugPauseMode.Asynchronous, "cancel")]
    [TestCase(LuaDebugPauseMode.Synchronous, "cancel")]
    public async Task HostDrivenResumeNotifiesTheClient(LuaDebugPauseMode mode, string action)
    {
        await using var host = await Host.CreateAsync(mode);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(host.Token);
        await host.InitializeAsync(stopOnEntry: true);
        await host.RequestAsync("configurationDone");
        var execution = Task.Run(async () =>
            await host.State.DoStringAsync("return 42", cancellationToken: cancellation.Token)
        );
        try
        {
            await host.EventAsync("stopped");
            if (action == "continue")
                host.Server.Debugger.Continue(host.Server.Debugger.CurrentStop!.Id);
            else if (action == "reset")
                host.Server.Debugger.Reset();
            else
                cancellation.Cancel();
            var continued = await host.EventAsync("continued");
            Assert.That(continued["body"]!["threadId"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(continued["body"]!["allThreadsContinued"]!.GetValue<bool>(), Is.True);
            if (action == "cancel")
                Assert.ThrowsAsync<LuaCanceledException>(async () => await execution);
            else
                Assert.That(
                    await execution.WaitAsync(host.Token),
                    Is.EqualTo(new LuaValue[] { 42 })
                );
        }
        finally
        {
            host.Server.Debugger.Reset();
            try
            {
                await execution;
            }
            catch (LuaCanceledException) when (cancellation.IsCancellationRequested) { }
        }
    }

    [TestCase(LuaDebugPauseMode.Asynchronous, false)]
    [TestCase(LuaDebugPauseMode.Asynchronous, true)]
    [TestCase(LuaDebugPauseMode.Synchronous, false)]
    [TestCase(LuaDebugPauseMode.Synchronous, true)]
    public async Task AttachingAdoptsAnExistingStop(LuaDebugPauseMode mode, bool stopOnEntry)
    {
        await using var host = await Host.CreateAsync(mode);
        var stopped = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        host.Server.Debugger.Stopped += _ => stopped.TrySetResult(true);
        host.Server.Debugger.Pause();
        var execution = Task.Run(async () =>
            await host.State.DoStringAsync("return 42", cancellationToken: host.Token)
        );
        try
        {
            await stopped.Task.WaitAsync(host.Token);
            await host.InitializeAsync(stopOnEntry);
            await host.RequestAsync("configurationDone");
            await host.EventAsync("stopped");
            var stack = await host.RequestAsync("stackTrace", new() { ["threadId"] = 1 });
            Assert.That(stack["body"]!["stackFrames"]!.AsArray(), Has.Count.EqualTo(1));
            await host.RequestAsync("continue", new() { ["threadId"] = 1 });
            Assert.That(await execution.WaitAsync(host.Token), Is.EqualTo(new LuaValue[] { 42 }));
        }
        finally
        {
            host.Server.Debugger.Reset();
            await execution;
        }
    }

    [TestCase(LuaDebugPauseMode.Asynchronous)]
    [TestCase(LuaDebugPauseMode.Synchronous)]
    public async Task BreakpointsStackScopesEvaluationAndVariableEditingWorkOverTcp(
        LuaDebugPauseMode mode
    )
    {
        await using var host = await Host.CreateAsync(mode);
        host.State.Environment[1] = 7;
        var source = "local x=1\nlocal t={name='é🐇'}\nx=x+2\nreturn x,t.name";
        var closure = host.State.Load(source, "@" + host.Path);
        var knownSource = host.Server.Debugger.RegisterSource(closure, source);
        await host.InitializeAsync();
        var points = await host.RequestAsync(
            "setBreakpoints",
            new()
            {
                ["source"] = new JsonObject { ["path"] = host.Path },
                ["breakpoints"] = new JsonArray(new JsonObject { ["line"] = 3 }),
            }
        );
        Assert.That(points["body"]!["breakpoints"]![0]!["verified"]!.GetValue<bool>(), Is.True);
        await host.RequestAsync("configurationDone");
        var execution = host.Execute(closure);
        var stopped = await host.EventAsync("stopped");
        Assert.That(stopped["body"]!["reason"]!.GetValue<string>(), Is.EqualTo("breakpoint"));
        var stack = await host.RequestAsync("stackTrace", new() { ["threadId"] = 1 });
        var frame = stack["body"]!["stackFrames"]![0]!;
        Assert.That(frame["line"]!.GetValue<int>(), Is.EqualTo(3));
        var frameId = frame["id"]!.GetValue<int>();
        var scopes = await host.RequestAsync("scopes", new() { ["frameId"] = frameId });
        var localsRef = scopes["body"]!["scopes"]![0]!["variablesReference"]!.GetValue<int>();
        var globalsRef = scopes["body"]!["scopes"]![2]!["variablesReference"]!.GetValue<int>();
        var globals = await host.RequestAsync(
            "variables",
            new() { ["variablesReference"] = globalsRef }
        );
        var numericGlobal = globals["body"]!["variables"]!
            .AsArray()
            .Single(v => v!["name"]!.GetValue<string>() == "[1]")!;
        Assert.That(
            numericGlobal["presentationHint"]!["attributes"]![0]!.GetValue<string>(),
            Is.EqualTo("readOnly")
        );
        await host.RequestAsync(
            "setVariable",
            new()
            {
                ["variablesReference"] = globalsRef,
                ["name"] = "[1]",
                ["value"] = "42",
            },
            expectSuccess: false
        );
        var variables = await host.RequestAsync(
            "variables",
            new() { ["variablesReference"] = localsRef }
        );
        var locals = variables["body"]!["variables"]!.AsArray();
        var tableRef = locals.Single(v => v!["name"]!.GetValue<string>() == "t")![
            "variablesReference"
        ]!.GetValue<int>();
        var children = await host.RequestAsync(
            "variables",
            new()
            {
                ["variablesReference"] = tableRef,
                ["start"] = 0,
                ["count"] = 1,
            }
        );
        Assert.That(
            children["body"]!["variables"]![0]!["value"]!.GetValue<string>(),
            Is.EqualTo("\"é🐇\"")
        );
        var evaluated = await host.RequestAsync(
            "evaluate",
            new() { ["frameId"] = frameId, ["expression"] = "x+4" }
        );
        Assert.That(evaluated["body"]!["result"]!.GetValue<string>(), Is.EqualTo("5"));
        await host.RequestAsync(
            "setVariable",
            new()
            {
                ["variablesReference"] = localsRef,
                ["name"] = "x",
                ["value"] = "10",
            }
        );
        var text = await host.RequestAsync(
            "source",
            new() { ["sourceReference"] = knownSource.Id }
        );
        Assert.That(text["body"]!["content"]!.GetValue<string>(), Is.EqualTo(source));
        await host.RequestAsync("continue", new() { ["threadId"] = 1 });
        Assert.That(
            await execution.WaitAsync(host.Token),
            Is.EqualTo(new LuaValue[] { 12, "é🐇" })
        );
        Assert.That(host.State.Environment[1], Is.EqualTo(new LuaValue(7)));
        Assert.That(host.State.Environment["[1]"], Is.EqualTo(LuaValue.Nil));
        var stale = await host.RequestAsync(
            "variables",
            new() { ["variablesReference"] = tableRef },
            expectSuccess: false
        );
        Assert.That(stale["success"]!.GetValue<bool>(), Is.False);
    }

    [Test]
    public async Task EntryAndSteppingUseDapResponseOrdering()
    {
        await using var host = await Host.CreateAsync();
        var closure = host.State.Load("local x=1\nx=x+1\nreturn x", "@" + host.Path);
        host.Server.Debugger.RegisterSource(closure);
        await host.InitializeAsync(stopOnEntry: true);
        await host.RequestAsync("configurationDone");
        var execution = host.Execute(closure);
        Assert.That(
            (await host.EventAsync("stopped"))["body"]!["reason"]!.GetValue<string>(),
            Is.EqualTo("entry")
        );
        await host.RequestAsync("next", new() { ["threadId"] = 1 });
        await host.EventAsync("stopped");
        var stack = await host.RequestAsync("stackTrace", new() { ["threadId"] = 1 });
        Assert.That(stack["body"]!["stackFrames"]![0]!["line"]!.GetValue<int>(), Is.EqualTo(2));
        await host.RequestAsync("stepIn", new() { ["threadId"] = 1 });
        await host.EventAsync("stopped");
        stack = await host.RequestAsync("stackTrace", new() { ["threadId"] = 1 });
        Assert.That(stack["body"]!["stackFrames"]![0]!["line"]!.GetValue<int>(), Is.EqualTo(3));
        await host.RequestAsync("continue", new() { ["threadId"] = 1 });
        Assert.That(await execution.WaitAsync(host.Token), Is.EqualTo(new LuaValue[] { 2 }));
    }

    [TestCase(false, LuaDebugPauseMode.Asynchronous)]
    [TestCase(true, LuaDebugPauseMode.Asynchronous)]
    [TestCase(false, LuaDebugPauseMode.Synchronous)]
    [TestCase(true, LuaDebugPauseMode.Synchronous)]
    public async Task DisconnectOrEofResumesTheHostAndAllowsReconnection(
        bool disconnectRequest,
        LuaDebugPauseMode mode
    )
    {
        await using var host = await Host.CreateAsync(mode);
        var closure = host.State.Load("return 42", "@" + host.Path);
        await host.InitializeAsync(stopOnEntry: true);
        await host.RequestAsync("configurationDone");
        var execution = host.Execute(closure);
        await host.EventAsync("stopped");
        if (disconnectRequest)
            await host.RequestAsync("disconnect");
        host.Client.Dispose();
        Assert.That(await execution.WaitAsync(host.Token), Is.EqualTo(new LuaValue[] { 42 }));
        await host.ReconnectAsync();
        await host.InitializeAsync();
        await host.RequestAsync("configurationDone");
        Assert.That(
            await host.Execute(closure).WaitAsync(host.Token),
            Is.EqualTo(new LuaValue[] { 42 })
        );
    }

    [Test]
    public async Task MalformedFrameDropsTheClientWithoutDetachingTheDebugger()
    {
        await using var host = await Host.CreateAsync();
        var closure = host.State.Load("return 42", "@" + host.Path);
        await host.InitializeAsync();
        await host.RequestAsync("configurationDone");
        await host
            .Client.GetStream()
            .WriteAsync("Content-Length: 8\r\n\r\nnot-json"u8.ToArray(), host.Token);
        host.Client.Dispose();
        await host.ReconnectAsync();
        await host.InitializeAsync();
        await host.RequestAsync("configurationDone");
        Assert.That(host.Server.Debugger.IsAttached, Is.True);
        Assert.That(
            await host.Execute(closure).WaitAsync(host.Token),
            Is.EqualTo(new LuaValue[] { 42 })
        );
    }

    [Test]
    public async Task StackTracePagingReportsTheTotalAndStableFrameIds()
    {
        await using var host = await Host.CreateAsync();
        var closure = host.State.Load(
            "local function inner()\n  return 1\nend\n"
                + "local function outer()\n  local value = inner()\n  return value\nend\n"
                + "local result = outer()\nreturn result",
            "@" + host.Path
        );
        host.Server.Debugger.RegisterSource(closure);
        await host.InitializeAsync();
        await host.RequestAsync(
            "setBreakpoints",
            new()
            {
                ["source"] = new JsonObject { ["path"] = host.Path },
                ["breakpoints"] = new JsonArray(new JsonObject { ["line"] = 2 }),
            }
        );
        await host.RequestAsync("configurationDone");
        var execution = host.Execute(closure);
        await host.EventAsync("stopped");

        var all = await host.RequestAsync("stackTrace", new() { ["threadId"] = 1 });
        var total = all["body"]!["totalFrames"]!.GetValue<int>();
        Assert.That(total, Is.EqualTo(3));

        var page = await host.RequestAsync(
            "stackTrace",
            new()
            {
                ["threadId"] = 1,
                ["startFrame"] = 1,
                ["levels"] = 2,
            }
        );
        Assert.That(page["body"]!["totalFrames"]!.GetValue<int>(), Is.EqualTo(3));
        var frames = page["body"]!["stackFrames"]!.AsArray();
        Assert.That(
            frames.Select(frame => frame!["id"]!.GetValue<int>()),
            Is.EqualTo(new[] { 2, 1 })
        );
        Assert.That(frames[0]!["line"]!.GetValue<int>(), Is.EqualTo(5));

        // The page must still resolve scopes for the frame it returned.
        var scopes = await host.RequestAsync("scopes", new() { ["frameId"] = 2 });
        Assert.That(scopes["body"]!["scopes"]!.AsArray().Count, Is.EqualTo(3));

        var beyond = await host.RequestAsync(
            "stackTrace",
            new()
            {
                ["threadId"] = 1,
                ["startFrame"] = 3,
                ["levels"] = 5,
            }
        );
        Assert.That(beyond["body"]!["totalFrames"]!.GetValue<int>(), Is.EqualTo(3));
        Assert.That(beyond["body"]!["stackFrames"]!.AsArray().Count, Is.Zero);
        var invalid = await host.RequestAsync(
            "scopes",
            new() { ["frameId"] = 9 },
            expectSuccess: false
        );
        Assert.That(invalid["message"]!.GetValue<string>(), Is.EqualTo("Unknown frame."));

        await host.RequestAsync("continue", new() { ["threadId"] = 1 });
        await execution.WaitAsync(host.Token);
    }

    [Test]
    public async Task CoordinatesAndUriPathsRespectClientInitialization()
    {
        await using var host = await Host.CreateAsync();
        var closure = host.State.Load("local x=1\nreturn x", "@" + host.Path);
        host.Server.Debugger.RegisterSource(closure);
        await host.InitializeAsync(zeroBased: true);
        await host.RequestAsync(
            "setBreakpoints",
            new()
            {
                ["source"] = new JsonObject { ["path"] = new Uri(host.Path).AbsoluteUri },
                ["breakpoints"] = new JsonArray(new JsonObject { ["line"] = 1 }),
            }
        );
        await host.RequestAsync("configurationDone");
        var execution = host.Execute(closure);
        await host.EventAsync("stopped");
        var stack = await host.RequestAsync("stackTrace", new() { ["threadId"] = 1 });
        var frame = stack["body"]!["stackFrames"]![0]!;
        Assert.That(frame["line"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(frame["column"]!.GetValue<int>(), Is.EqualTo(0));
        Assert.That(
            frame["source"]!["path"]!.GetValue<string>(),
            Is.EqualTo(new Uri(host.Path).AbsoluteUri)
        );
        await host.RequestAsync("continue", new() { ["threadId"] = 1 });
        await execution.WaitAsync(host.Token);
    }

    [Test]
    public async Task FragmentedUnicodeRequestsAndUnsupportedCommandsDoNotBreakTheSession()
    {
        await using var host = await Host.CreateAsync();
        await host.InitializeAsync(fragment: true);
        var response = await host.RequestAsync(
            "launch",
            new() { ["program"] = "é🐇.lua" },
            expectSuccess: false
        );
        Assert.That(response["success"]!.GetValue<bool>(), Is.False);
        var threads = await host.RequestAsync("threads");
        Assert.That(threads["body"]!["threads"]!.AsArray(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task DisconnectCancelsAnAwaitingEvaluationWithoutCancellingHostExecution()
    {
        await using var host = await Host.CreateAsync();
        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var waiting = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        host.State.Environment["wait"] = new LuaFunction(
            async (context, token) =>
            {
                entered.TrySetResult(true);
                await waiting.Task.WaitAsync(token);
                return context.Return();
            }
        );
        var closure = host.State.Load("return 42", "@" + host.Path);
        await host.InitializeAsync(stopOnEntry: true);
        await host.RequestAsync("configurationDone");
        var execution = host.Execute(closure);
        await host.EventAsync("stopped");
        await host.SendAsync("evaluate", new() { ["frameId"] = 1, ["expression"] = "wait()" });
        await entered.Task.WaitAsync(host.Token);
        host.Client.Dispose();
        Assert.That(await execution.WaitAsync(host.Token), Is.EqualTo(new LuaValue[] { 42 }));
    }

    sealed class Host : IAsyncDisposable
    {
        readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(15));
        DapClient protocol = null!;
        Task<LuaValue[]>? execution;
        Task listening = null!;
        public LuaState State { get; } = LuaState.Create();
        public LuaDapServer Server { get; }
        public TcpClient Client { get; private set; } = null!;
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LuaDapTests", "test.lua");
        public CancellationToken Token => lifetime.Token;
        public IReadOnlyList<string> ReceivedEvents => protocol.ReceivedEvents;

        Host(LuaDebugPauseMode mode)
        {
            State.OpenStandardLibraries();
            Server = new LuaDapServer(
                State,
                port: 0,
                bindAddress: IPAddress.Loopback,
                debuggerOptions: new LuaDebuggerOptions { PauseMode = mode }
            );
        }

        public static async Task<Host> CreateAsync(
            LuaDebugPauseMode mode = LuaDebugPauseMode.Asynchronous
        )
        {
            var host = new Host(mode);
            host.listening = host.Server.RunAsync(host.Token);
            await host.ReconnectAsync();
            return host;
        }

        public async Task ReconnectAsync()
        {
            Client = new TcpClient { NoDelay = true };
            await Client.ConnectAsync(IPAddress.Loopback, Server.LocalEndpoint.Port, Token);
            protocol = new DapClient(Client.GetStream(), Token);
        }

        public Task<LuaValue[]> Execute(Runtime.LuaClosure closure) =>
            execution = Task.Run(() => ExecuteConfiguredAsync(closure));

        async Task<LuaValue[]> ExecuteConfiguredAsync(Runtime.LuaClosure closure)
        {
            await Server.WaitForConfigurationAsync(Token);
            return await State.ExecuteAsync(closure, Token);
        }

        public async Task InitializeAsync(
            bool stopOnEntry = false,
            bool zeroBased = false,
            bool fragment = false
        )
        {
            var id = await SendAsync(
                "initialize",
                new()
                {
                    ["adapterID"] = "é🐇",
                    ["linesStartAt1"] = !zeroBased,
                    ["columnsStartAt1"] = !zeroBased,
                    ["pathFormat"] = zeroBased ? "uri" : "path",
                },
                fragment
            );
            await protocol.ResponseAsync(id, true);
            await EventAsync("initialized");
            await RequestAsync("attach", new() { ["stopOnEntry"] = stopOnEntry });
        }

        public Task<JsonObject> RequestAsync(
            string command,
            JsonObject? args = null,
            bool expectSuccess = true
        ) => protocol.RequestAsync(command, args, expectSuccess);

        public Task<int> SendAsync(
            string command,
            JsonObject? args = null,
            bool fragment = false
        ) => protocol.SendAsync(command, args, fragment);

        public Task<JsonObject> EventAsync(string name) => protocol.EventAsync(name);

        public async ValueTask DisposeAsync()
        {
            lifetime.Cancel();
            Client.Dispose();
            Server.Dispose();
            await listening;
            if (execution != null)
                try
                {
                    await execution;
                }
                catch (LuaCanceledException) { }
            State.Dispose();
            lifetime.Dispose();
        }
    }
}
