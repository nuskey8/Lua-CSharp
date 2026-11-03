using System.Text.Json.Nodes;
using System.Threading.Channels;
using Lua.Debugging;

namespace Lua.Debugger.Dap;

/// <summary>A single attach session over caller-owned streams. The protocol and VM pumps run independently.</summary>
public sealed partial class LuaDapSession
{
    readonly LuaDebugger debugger;
    readonly DapConnection connection;
    readonly Channel<JsonObject> events = Channel.CreateUnbounded<JsonObject>(
        new UnboundedChannelOptions { SingleReader = true }
    );
    readonly Dictionary<int, VariableReference> references = new();
    readonly object referenceGate = new();
    bool initialized;
    bool attached;
    bool configured;
    bool stopOnEntry;
    bool entryPending;
    bool linesStartAtOne = true;
    bool columnsStartAtOne = true;
    bool uriPaths;
    int stopId;
    int nextReference;
    int running;

    public LuaDapSession(LuaDebugger debugger, Stream input, Stream output)
    {
        this.debugger = debugger;
        connection = new(input, output);
    }

    public event Action? Configured;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref running, 1) != 0)
            throw new InvalidOperationException("The DAP session has already started.");
        using var lifecycle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var requests = Channel.CreateUnbounded<PendingRequest>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }
        );
        var inspections = Channel.CreateUnbounded<PendingRequest>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }
        );
        debugger.Stopped += OnStopped;
        debugger.StopEnded += OnStopEnded;
        debugger.BreakpointChanged += OnBreakpointChanged;
        var reader = ReadRequestsAsync(requests.Writer, lifecycle);
        var dispatcher = DispatchRequestsAsync(
            requests.Reader,
            inspections.Writer,
            lifecycle.Token
        );
        var inspector = InspectRequestsAsync(inspections.Reader, lifecycle.Token);
        var sender = SendEventsAsync(lifecycle.Token);
        try
        {
            await await Task.WhenAny(reader, dispatcher, inspector, sender).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifecycle.IsCancellationRequested) { }
        finally
        {
            try
            {
                StopSession(lifecycle);
            }
            finally
            {
                try
                {
                    await Task.WhenAll(reader, dispatcher, inspector, sender).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (lifecycle.IsCancellationRequested) { }
                finally
                {
                    ResetSession();
                }
            }
        }
    }

    void StopSession(CancellationTokenSource lifecycle)
    {
        debugger.Stopped -= OnStopped;
        debugger.StopEnded -= OnStopEnded;
        debugger.BreakpointChanged -= OnBreakpointChanged;
        events.Writer.TryComplete();
        try
        {
            lifecycle.Cancel();
        }
        finally
        {
            ResetSession();
        }
    }

    void ResetSession()
    {
        lock (referenceGate)
        {
            configured = false;
            stopId = 0;
            references.Clear();
        }
        debugger.Reset();
    }

    public Task NotifyTerminatedAsync(CancellationToken cancellationToken = default)
    {
        debugger.Reset();
        return connection.WriteAsync(Event("terminated", new JsonObject()), cancellationToken);
    }

    async Task ReadRequestsAsync(
        ChannelWriter<PendingRequest> requests,
        CancellationTokenSource lifecycle
    )
    {
        try
        {
            while (await connection.ReadAsync(lifecycle.Token).ConfigureAwait(false) is { } request)
            {
                if (
                    request["type"] is not JsonValue type
                    || !type.TryGetValue<string>(out var messageType)
                )
                    throw new InvalidDataException("Missing DAP message type.");
                if (messageType != "request")
                    continue;
                if (
                    request["seq"] is not JsonValue sequence
                    || !sequence.TryGetValue<int>(out _)
                    || request["command"] is not JsonValue command
                    || !command.TryGetValue<string>(out _)
                )
                    throw new InvalidDataException("Invalid DAP request.");
                if (Text(request, "command") == "disconnect")
                {
                    await RespondAsync(request, new JsonObject(), lifecycle.Token)
                        .ConfigureAwait(false);
                    return;
                }
                await requests
                    .WriteAsync(
                        new PendingRequest(request, Volatile.Read(ref stopId)),
                        lifecycle.Token
                    )
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            requests.TryComplete();
            lifecycle.Cancel();
        }
    }

    async Task DispatchRequestsAsync(
        ChannelReader<PendingRequest> requests,
        ChannelWriter<PendingRequest> inspections,
        CancellationToken token
    )
    {
        try
        {
            while (await requests.WaitToReadAsync(token).ConfigureAwait(false))
            while (requests.TryRead(out var request))
            {
                if (
                    Text(request.Message, "command")
                    is "stackTrace"
                        or "scopes"
                        or "variables"
                        or "evaluate"
                        or "setVariable"
                        or "source"
                )
                    await inspections.WriteAsync(request, token).ConfigureAwait(false);
                else
                    await HandleRequestAsync(request, token).ConfigureAwait(false);
            }
        }
        finally
        {
            inspections.TryComplete();
        }
    }

    async Task InspectRequestsAsync(ChannelReader<PendingRequest> requests, CancellationToken token)
    {
        while (await requests.WaitToReadAsync(token).ConfigureAwait(false))
        while (requests.TryRead(out var request))
            await HandleRequestAsync(request, token).ConfigureAwait(false);
    }

    async Task HandleRequestAsync(PendingRequest request, CancellationToken token)
    {
        try
        {
            await DispatchAsync(request.Message, request.StopId, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await connection
                .WriteAsync(
                    new JsonObject
                    {
                        ["type"] = "response",
                        ["request_seq"] = Integer(request.Message, "seq"),
                        ["command"] = Text(request.Message, "command"),
                        ["success"] = false,
                        ["message"] = exception.Message,
                    },
                    token
                )
                .ConfigureAwait(false);
        }
    }

    async Task SendEventsAsync(CancellationToken token)
    {
        while (await events.Reader.WaitToReadAsync(token).ConfigureAwait(false))
        while (events.Reader.TryRead(out var message))
            await connection.WriteAsync(message, token).ConfigureAwait(false);
    }

    void OnStopped(LuaDebugStop stop)
    {
        lock (referenceGate)
        {
            if (!configured || stopId == stop.Id)
                return;
            stopId = stop.Id;
            references.Clear();
            var reason = entryPending ? "entry" : stop.Reason;
            entryPending = false;
            var body = new JsonObject
            {
                ["reason"] = reason,
                ["threadId"] = 1,
                ["allThreadsStopped"] = true,
            };
            if (stop.BreakpointId is { } id)
                body["hitBreakpointIds"] = new JsonArray(id);
            events.Writer.TryWrite(Event("stopped", body));
        }
    }

    void OnBreakpointChanged(LuaBreakpoint breakpoint) =>
        events.Writer.TryWrite(
            Event(
                "breakpoint",
                new JsonObject { ["reason"] = "changed", ["breakpoint"] = ToBreakpoint(breakpoint) }
            )
        );

    void OnStopEnded(int id)
    {
        lock (referenceGate)
            if (stopId == id)
            {
                stopId = 0;
                references.Clear();
                events.Writer.TryWrite(
                    Event(
                        "continued",
                        new JsonObject { ["threadId"] = 1, ["allThreadsContinued"] = true }
                    )
                );
            }
    }

    int AddReference(VariableReference value, int expectedStop)
    {
        lock (referenceGate)
        {
            CurrentStop(expectedStop);
            var id = ++nextReference;
            references.Add(id, value);
            return id;
        }
    }

    VariableReference GetReference(int id)
    {
        lock (referenceGate)
            return references.TryGetValue(id, out var reference)
                ? reference
                : throw new ArgumentException("The variable reference is no longer valid.");
    }

    int CurrentStop(int expected)
    {
        lock (referenceGate)
            return stopId != 0 && expected == stopId
                ? stopId
                : throw new InvalidOperationException(
                    "Lua execution is not stopped at this location."
                );
    }

    readonly struct PendingRequest(JsonObject message, int stopId)
    {
        public readonly JsonObject Message = message;
        public readonly int StopId = stopId;
    }

    sealed class VariableReference
    {
        public readonly int FrameId;
        public readonly LuaDebugScope? Scope;
        public readonly LuaValue Value;

        public VariableReference(int frame, LuaDebugScope scope)
        {
            FrameId = frame;
            Scope = scope;
        }

        public VariableReference(LuaValue value)
        {
            Value = value;
        }
    }
}
