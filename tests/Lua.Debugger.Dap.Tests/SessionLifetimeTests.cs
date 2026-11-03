using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Lua.Debugging;
using Lua.Runtime;
using NUnit.Framework;

namespace Lua.Debugger.Dap.Tests;

public class SessionLifetimeTests
{
    [TestCase(LuaDebugPauseMode.Asynchronous)]
    [TestCase(LuaDebugPauseMode.Synchronous)]
    public async Task OutputFailureDrainsAndResetsDespiteThrowingCancellationCallbacks(
        LuaDebugPauseMode mode
    )
    {
        await using var host = await SessionHost.CreateAsync(mode);
        var entered = Signal();
        var cleanupEntered = Signal();
        var cleanupRelease = Signal();
        var drained = false;
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
                    cleanupEntered.TrySetResult(true);
                    await cleanupRelease.Task.WaitAsync(host.Token);
                    drained = true;
                }
            }
        );
        await host.ConfigureAsync(stopOnEntry: true);
        var execution = host.Execute("return 42");
        await host.Protocol.EventAsync("stopped");
        await host.Protocol.SendAsync(
            "evaluate",
            new() { ["frameId"] = 1, ["expression"] = "wait()" }
        );
        await entered.Task.WaitAsync(host.Token);
        try
        {
            host.Output.FailWrites = true;
            await host.Protocol.SendAsync("threads");
            await cleanupEntered.Task.WaitAsync(host.Token);
            cleanupRelease.TrySetResult(true);
            var failure = Assert.ThrowsAsync<IOException>(async () =>
                await host.Running.WaitAsync(host.Token)
            );
            Assert.That(failure!.Message, Is.EqualTo("output failed"));
            Assert.That(drained, Is.True);
            Assert.That(await execution.WaitAsync(host.Token), Is.EqualTo(new LuaValue[] { 42 }));
            Assert.That(host.Debugger.CurrentStop, Is.Null);
            var stops = 0;
            host.Debugger.Stopped += _ => stops++;
            Assert.That(
                await host.State.DoStringAsync("return 43"),
                Is.EqualTo(new LuaValue[] { 43 })
            );
            Assert.That(stops, Is.Zero);
        }
        finally
        {
            cleanupRelease.TrySetResult(true);
            host.Debugger.Reset();
        }
    }

    [TestCase("pause")]
    [TestCase("configurationDone")]
    public async Task DisconnectDuringResponseCannotRearmDebuggerControls(string command)
    {
        await using var host = await SessionHost.CreateAsync(
            LuaDebugPauseMode.Asynchronous,
            command
        );
        host.Debugger.Pause();
        var execution = host.Execute("return 42");
        await host.Stops.Reader.ReadAsync(host.Token);
        await host.InitializeAsync(stopOnEntry: true);
        var response = host.Protocol.RequestAsync(command);
        await host.Output.Entered.Task.WaitAsync(host.Token);
        await response;
        host.Client.Client.Shutdown(SocketShutdown.Send);
        Assert.That(await execution.WaitAsync(host.Token), Is.EqualTo(new LuaValue[] { 42 }));
        host.Output.Release.TrySetResult(true);
        await host.Running.WaitAsync(host.Token);
        Assert.That(host.Configured, Is.False);
        var stops = 0;
        host.Debugger.Stopped += stop =>
        {
            stops++;
            host.Debugger.Continue(stop.Id);
        };
        Assert.That(await host.State.DoStringAsync("return 43"), Is.EqualTo(new LuaValue[] { 43 }));
        Assert.That(stops, Is.Zero);
    }

    [TestCase(LuaDebugPauseMode.Asynchronous)]
    [TestCase(LuaDebugPauseMode.Synchronous)]
    public async Task LateResumeResponsePreservesANewerStop(LuaDebugPauseMode mode)
    {
        await using var host = await SessionHost.CreateAsync(mode, "continue");
        var closure = host.State.Load("local x=1\nreturn x", "=resume-race");
        host.Debugger.RegisterSource(closure);
        host.Debugger.SetBreakpoints("=resume-race", [1, 2]);
        await host.ConfigureAsync();
        var execution = host.Execute(closure);
        var first = await host.Stops.Reader.ReadAsync(host.Token);
        await host.Protocol.EventAsync("stopped");
        var response = host.Protocol.RequestAsync("continue", new() { ["threadId"] = 1 });
        await host.Output.Entered.Task.WaitAsync(host.Token);
        await response;
        host.Debugger.Continue(first.Id);
        var second = await host.Stops.Reader.ReadAsync(host.Token);
        host.Output.Release.TrySetResult(true);
        await host.Protocol.RequestAsync("threads");
        var stack = await host.Protocol.RequestAsync("stackTrace", new() { ["threadId"] = 1 });
        Assert.That(stack["body"]!["stackFrames"]![0]!["line"]!.GetValue<int>(), Is.EqualTo(2));
        Assert.That(host.Debugger.CurrentStop!.Id, Is.EqualTo(second.Id));
        await host.Protocol.RequestAsync("continue", new() { ["threadId"] = 1 });
        Assert.That(await execution.WaitAsync(host.Token), Is.EqualTo(new LuaValue[] { 1 }));
    }

    [TestCase(LuaDebugPauseMode.Asynchronous, "continue", false)]
    [TestCase(LuaDebugPauseMode.Synchronous, "continue", false)]
    [TestCase(LuaDebugPauseMode.Asynchronous, "next", false)]
    [TestCase(LuaDebugPauseMode.Synchronous, "next", false)]
    [TestCase(LuaDebugPauseMode.Asynchronous, "stepIn", false)]
    [TestCase(LuaDebugPauseMode.Synchronous, "stepIn", false)]
    [TestCase(LuaDebugPauseMode.Asynchronous, "stepOut", false)]
    [TestCase(LuaDebugPauseMode.Synchronous, "stepOut", false)]
    [TestCase(LuaDebugPauseMode.Asynchronous, "continue", true)]
    [TestCase(LuaDebugPauseMode.Synchronous, "continue", true)]
    public async Task ResumeCancelsAnAwaitingEvaluation(
        LuaDebugPauseMode mode,
        string command,
        bool throwOnCancellation
    )
    {
        await using var host = await SessionHost.CreateAsync(mode);
        var entered = Signal();
        var drained = false;
        host.State.Environment["wait"] = new LuaFunction(
            async (context, token) =>
            {
                var waiting = new TaskCompletionSource<bool>().Task.WaitAsync(token);
                using var registration = token.Register(() =>
                {
                    if (throwOnCancellation)
                        throw new InvalidOperationException("cancellation callback failed");
                });
                try
                {
                    entered.TrySetResult(true);
                    await waiting;
                    return context.Return();
                }
                finally
                {
                    drained = true;
                }
            }
        );
        await host.ConfigureAsync(stopOnEntry: true);
        var execution = host.Execute("local x=42\nreturn x");
        await host.Protocol.EventAsync("stopped");
        var evaluation = await host.Protocol.SendAsync(
            "evaluate",
            new() { ["frameId"] = 1, ["expression"] = "wait()" }
        );
        await entered.Task.WaitAsync(host.Token);
        await host.Protocol.RequestAsync(command, new() { ["threadId"] = 1 });
        await host.Protocol.ResponseAsync(evaluation, false);
        Assert.That(drained, Is.True);
        if (throwOnCancellation)
        {
            var output = await host.Protocol.EventAsync("output");
            Assert.That(
                output["body"]!["output"]!.GetValue<string>(),
                Does.Contain("cancellation callback failed")
            );
        }
        if (command is "next" or "stepIn")
        {
            await host.Protocol.EventAsync("stopped");
            var stack = await host.Protocol.RequestAsync("stackTrace", new() { ["threadId"] = 1 });
            Assert.That(stack["body"]!["stackFrames"]![0]!["line"]!.GetValue<int>(), Is.EqualTo(2));
            await host.Protocol.RequestAsync("continue", new() { ["threadId"] = 1 });
        }
        Assert.That(await execution.WaitAsync(host.Token), Is.EqualTo(new LuaValue[] { 42 }));
        await host.Protocol.RequestAsync("threads");
        Assert.That(host.Protocol.ReceivedEvents, Does.Not.Contain("continued"));
    }

    [TestCase(LuaDebugPauseMode.Asynchronous)]
    [TestCase(LuaDebugPauseMode.Synchronous)]
    public async Task QueuedEvaluationCannotRunAgainstALaterStop(LuaDebugPauseMode mode)
    {
        await using var host = await SessionHost.CreateAsync(mode, "evaluate");
        var entered = Signal();
        var mutated = false;
        host.State.Environment["wait"] = new LuaFunction(
            async (context, token) =>
            {
                entered.TrySetResult(true);
                await new TaskCompletionSource<bool>().Task.WaitAsync(token);
                return context.Return();
            }
        );
        host.State.Environment["mutate"] = new LuaFunction(
            (context, _) =>
            {
                mutated = true;
                return new(context.Return());
            }
        );
        var closure = host.State.Load("local x=1\nx=x+1\nreturn x", "=queued-evaluation");
        host.Debugger.RegisterSource(closure);
        host.Debugger.SetBreakpoints("=queued-evaluation", [2]);
        await host.ConfigureAsync();
        var execution = host.Execute(closure);
        await host.Stops.Reader.ReadAsync(host.Token);
        await host.Protocol.EventAsync("stopped");
        var first = await host.Protocol.SendAsync(
            "evaluate",
            new() { ["frameId"] = 1, ["expression"] = "wait()" }
        );
        await entered.Task.WaitAsync(host.Token);
        var queued = await host.Protocol.SendAsync(
            "evaluate",
            new() { ["frameId"] = 1, ["expression"] = "mutate()" }
        );
        await host.Protocol.RequestAsync("next", new() { ["threadId"] = 1 });
        await host.Output.Entered.Task.WaitAsync(host.Token);
        await host.Stops.Reader.ReadAsync(host.Token);
        await host.Protocol.ResponseAsync(first, false);
        host.Output.Release.TrySetResult(true);
        await host.Protocol.ResponseAsync(queued, false);
        Assert.That(mutated, Is.False);
        await host.Protocol.EventAsync("stopped");
        await host.Protocol.RequestAsync("continue", new() { ["threadId"] = 1 });
        Assert.That(await execution.WaitAsync(host.Token), Is.EqualTo(new LuaValue[] { 2 }));
    }

    static TaskCompletionSource<bool> Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    sealed class SessionHost : IAsyncDisposable
    {
        readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(10));
        readonly TaskCompletionSource<bool> configuration = Signal();
        TcpClient peer = null!;
        Task<LuaValue[]>? execution;
        public LuaState State { get; } = LuaState.Create();
        public LuaDebugger Debugger { get; }
        public TcpClient Client { get; } = new() { NoDelay = true };
        public ControlledOutput Output { get; private set; } = null!;
        public DapClient Protocol { get; private set; } = null!;
        public Channel<LuaDebugStop> Stops { get; } = Channel.CreateUnbounded<LuaDebugStop>();
        public Task Running { get; private set; } = Task.CompletedTask;
        public bool Configured => configuration.Task.IsCompletedSuccessfully;
        public CancellationToken Token => lifetime.Token;

        SessionHost(LuaDebugPauseMode mode)
        {
            Debugger = new LuaDebugger(State, new LuaDebuggerOptions { PauseMode = mode });
            Debugger.Attach();
        }

        public static async Task<SessionHost> CreateAsync(
            LuaDebugPauseMode mode,
            string delayCommand = ""
        )
        {
            var host = new SessionHost(mode);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var connect = host.Client.ConnectAsync(
                    IPAddress.Loopback,
                    ((IPEndPoint)listener.LocalEndpoint).Port,
                    host.Token
                );
                host.peer = await listener.AcceptTcpClientAsync(host.Token);
                await connect;
                host.peer.NoDelay = true;
                host.Output = new ControlledOutput(host.peer.GetStream(), delayCommand);
                host.Protocol = new DapClient(host.Client.GetStream(), host.Token);
                var session = new LuaDapSession(host.Debugger, host.peer.GetStream(), host.Output);
                session.Configured += () => host.configuration.TrySetResult(true);
                host.Running = session.RunAsync(host.Token);
                host.Debugger.Stopped += stop => host.Stops.Writer.TryWrite(stop);
                return host;
            }
            catch
            {
                await host.DisposeAsync();
                throw;
            }
            finally
            {
                listener.Stop();
            }
        }

        public async Task InitializeAsync(bool stopOnEntry = false)
        {
            await Protocol.RequestAsync("initialize");
            await Protocol.RequestAsync("attach", new() { ["stopOnEntry"] = stopOnEntry });
        }

        public async Task ConfigureAsync(bool stopOnEntry = false)
        {
            await InitializeAsync(stopOnEntry);
            await Protocol.RequestAsync("configurationDone");
            await configuration.Task.WaitAsync(Token);
        }

        public Task<LuaValue[]> Execute(string source) => Execute(State.Load(source, "=session"));

        public Task<LuaValue[]> Execute(LuaClosure closure) =>
            execution = Task.Run(async () => await State.ExecuteAsync(closure, Token));

        public async ValueTask DisposeAsync()
        {
            Output?.Release.TrySetResult(true);
            lifetime.Cancel();
            Debugger.Reset();
            try
            {
                await Task.WhenAll(
                    execution == null ? [Running] : new Task[] { Running, execution }
                );
            }
            catch (LuaCanceledException) { }
            catch (IOException) when (Output?.FailWrites == true) { }
            finally
            {
                Client.Dispose();
                peer?.Dispose();
                Debugger.Dispose();
                State.Dispose();
                lifetime.Dispose();
            }
        }
    }

    sealed class ControlledOutput(Stream inner, string command) : Stream
    {
        bool delay;
        public volatile bool FailWrites;
        public readonly TaskCompletionSource<bool> Entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        public readonly TaskCompletionSource<bool> Release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            if (FailWrites)
                throw new IOException("output failed");
            await inner.WriteAsync(buffer, cancellationToken);
            delay = Encoding
                .UTF8.GetString(buffer.Span)
                .Contains($"\"command\":\"{command}\"", StringComparison.Ordinal);
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await inner.FlushAsync(cancellationToken);
            if (delay)
            {
                delay = false;
                Entered.TrySetResult(true);
                await Release.Task;
            }
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            inner.Write(buffer, offset, count);
    }
}
