using System.Net;
using System.Net.Sockets;
using Lua.Debugging;

namespace Lua.Debugger.Dap;

/// <summary>A C# DAP listener for an existing Lua host. Serves one client at a time and supports reconnecting.</summary>
public sealed class LuaDapServer : IDisposable
{
    readonly TcpListener listener;
    readonly CancellationTokenSource shutdown = new();
    readonly TaskCompletionSource<bool> configured = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    TcpClient? client;
    LuaDapSession? session;
    int started;
    int disposed;

    public LuaDapServer(LuaState state, int port = 4711, IPAddress? bindAddress = null)
        : this(state, null, port, bindAddress) { }

    public LuaDapServer(
        LuaState state,
        LuaDebuggerOptions? debuggerOptions,
        int port = 4711,
        IPAddress? bindAddress = null
    )
    {
        listener = new(bindAddress ?? IPAddress.Loopback, port);
        Debugger = new LuaDebugger(state, debuggerOptions);
    }

    public LuaDebugger Debugger { get; }
    public IPEndPoint LocalEndpoint => (IPEndPoint)listener.LocalEndpoint;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref started, 1) != 0)
            throw new InvalidOperationException("The DAP server has already started.");
        using var lifecycle = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            shutdown.Token
        );
        using var registration = lifecycle.Token.Register(listener.Stop);
        try
        {
            lifecycle.Token.ThrowIfCancellationRequested();
            Debugger.Attach();
            listener.Start();
            while (!lifecycle.IsCancellationRequested)
            {
                using var connected = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                client = connected;
                connected.NoDelay = true;
                using var disconnect = lifecycle.Token.Register(connected.Dispose);
                var stream = connected.GetStream();
                var current = new LuaDapSession(Debugger, stream, stream);
                current.Configured += OnConfigured;
                Volatile.Write(ref session, current);
                try
                {
                    await current.RunAsync(lifecycle.Token).ConfigureAwait(false);
                }
                catch (IOException) { }
                catch (SocketException) { }
                finally
                {
                    Volatile.Write(ref session, null);
                    current.Configured -= OnConfigured;
                    client = null;
                }
            }
        }
        catch (Exception exception)
            when (lifecycle.IsCancellationRequested
                && exception
                    is OperationCanceledException
                        or SocketException
                        or ObjectDisposedException
            ) { }
        finally
        {
            listener.Stop();
            Debugger.Detach();
            configured.TrySetCanceled();
        }
    }

    void OnConfigured() => configured.TrySetResult(true);

    /// <summary>Waits for the first client to finish configuring breakpoints before the host starts its script.</summary>
    public async Task WaitForConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var cancelled = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var registration = cancellationToken.Register(() =>
            cancelled.TrySetCanceled(cancellationToken)
        );
        await await Task.WhenAny(configured.Task, cancelled.Task).ConfigureAwait(false);
    }

    /// <summary>Notifies the client that the host's script completed; the host retains control of process lifetime.</summary>
    public Task NotifyTerminatedAsync(CancellationToken cancellationToken = default) =>
        Volatile.Read(ref session)?.NotifyTerminatedAsync(cancellationToken) ?? Task.CompletedTask;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        configured.TrySetCanceled();
        try
        {
            shutdown.Cancel();
        }
        finally
        {
            listener.Stop();
            client?.Dispose();
            Debugger.Dispose();
        }
    }
}
