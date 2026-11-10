using System.Runtime.CompilerServices;
using Lua.Runtime;

namespace Lua.Debugging;

/// <summary>
/// An opt-in debugger shared by a Lua state and its coroutines. Inspection runs on
/// the suspended execution, never concurrently against its stack. It does not own the state.
/// </summary>
public sealed partial class LuaDebugger : IDisposable, ILuaDebugSession
{
    readonly object identity = new();
    readonly LuaState root;
    readonly LuaDebuggerOptions options;
    readonly object gate = new();
    readonly Queue<Command> commands = new();
    TaskCompletionSource<bool> signal = NewSignal();
    PauseContext? paused;
    StepRequest? step;
    bool pauseRequested;
    bool resumeRequested;
    bool evaluating;
    bool disposed;
    int stoppedThreadId = -1;
    int nextBreakpointId;
    int nextStopId;
    int nextFrameId;

    public LuaDebugger(LuaState state)
        : this(state, null) { }

    public LuaDebugger(LuaState state, LuaDebuggerOptions? options)
    {
        root = state.MainThread;
        this.options = options ?? new LuaDebuggerOptions();
        breakpoints = new(sources.Comparer);
    }

    /// <summary>Raised on the execution thread once the stack is available for inspection.</summary>
    /// <remarks>
    /// Inspections are serviced by the paused execution after this callback returns. Do not block
    /// on them here; <see cref="GetVariablesAsync"/> and friends throw instead of deadlocking.
    /// </remarks>
    public event Action<LuaDebugStop>? Stopped;

    /// <summary>Raised after a stop and its inspections unwind, including when disposed during a stop.</summary>
    public event Action<int>? StopEnded;
    public event Action<LuaBreakpoint>? BreakpointChanged;
    object ILuaDebugSession.Identity => identity;

    public bool IsAttached => ReferenceEquals(Volatile.Read(ref root.GlobalState.Debugger), this);
    public LuaDebugStop? CurrentStop
    {
        get
        {
            lock (gate)
                // A stop that is already resuming is no longer adoptable; RequireStop and
                // TryContinue use the same predicate.
                return resumeRequested ? null : paused?.Stop;
        }
    }

    public void Attach()
    {
        lock (gate)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(LuaDebugger));
            if (!IsAttached && Volatile.Read(ref root.GlobalState.ActiveExecutions) != 0)
                throw new InvalidOperationException(
                    "Install the debugger before starting Lua execution."
                );
            var previous = Interlocked.CompareExchange(ref root.GlobalState.Debugger, this, null);
            if (previous != null && previous != this)
                throw new InvalidOperationException(
                    "A debugger is already attached to this Lua state."
                );
        }
    }

    /// <summary>Detaches and releases a stop without cancelling host execution.</summary>
    public void Detach() => Release(detach: true);

    /// <summary>Clears session controls and releases a stop, retaining the debugger attachment for reconnecting.</summary>
    public void Reset() => Release(detach: false);

    void Release(bool detach)
    {
        CancellationTokenSource? inspections;
        lock (gate)
        {
            if (detach)
                Interlocked.CompareExchange(ref root.GlobalState.Debugger, null, this);
            ClearBreakpointsCore();
            step = null;
            pauseRequested = false;
            resumeRequested = true;
            UpdateExecutionCodes();
            signal.TrySetResult(true);
            inspections = paused?.Inspections;
        }
        CancelInspections(inspections);
    }

    /// <summary>Requests a stop at the next source/control-flow checkpoint. It cannot interrupt host C# code.</summary>
    public void Pause()
    {
        lock (gate)
        {
            if (!IsAttached)
                throw new InvalidOperationException("The debugger is not attached.");
            pauseRequested = true;
            UpdateExecutionCodes();
        }
    }

    /// <summary>Resumes the specified stop. Stale stop IDs cannot resume a later stop.</summary>
    public void Continue(int stopId, LuaDebugStep mode = LuaDebugStep.Continue)
    {
        if (!TryContinue(stopId, mode))
            throw new InvalidOperationException("The debug stop is no longer active.");
    }

    internal bool TryContinue(int stopId, LuaDebugStep mode)
    {
        CancellationTokenSource inspections;
        lock (gate)
        {
            if (paused is not { } context || context.Stop.Id != stopId || resumeRequested)
                return false;
            step =
                mode == LuaDebugStep.Continue
                    ? null
                    : new StepRequest(mode, context.State, context.Depth, context.FrameId);
            resumeRequested = true;
            UpdateExecutionCodes();
            signal.TrySetResult(true);
            inspections = context.Inspections;
        }
        CancelInspections(inspections);
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    ValueTask<int> ILuaDebugSession.CheckpointAsync(
        LuaState state,
        LuaClosure closure,
        int pc,
        int line,
        bool lineEntry,
        CancellationToken cancellationToken
    )
    {
        if (evaluating || state.IsInHook)
            return new(0);
        PauseContext context;
        lock (gate)
        {
            if (paused != null || !IsAttached)
                return new(0);
            var entry = registered.GetValue(
                closure.Proto,
                _ => throw new InvalidOperationException()
            );
            LuaBreakpoint? breakpoint = null;
            if (lineEntry)
                entry.Points.TryGetValue(line, out breakpoint);
            var stopForStep = lineEntry && step is { } request && ShouldStep(state, request);
            if (!pauseRequested && !stopForStep && breakpoint == null)
                return new(0);
            var reason =
                pauseRequested ? "pause"
                : breakpoint != null ? "breakpoint"
                : "step";
            pauseRequested = false;
            step = null;
            var stop = new LuaDebugStop(++nextStopId, reason, entry.Source, line, breakpoint?.Id);
            paused = context = new PauseContext(
                state,
                pc,
                state.CallStackFrameCount,
                state.GetDebugFrameId(++nextFrameId),
                stop
            );
            resumeRequested = false;
            signal = NewSignal();
            UpdateExecutionCodes();
        }
        return options.PauseMode == LuaDebugPauseMode.Synchronous
            ? SuspendSynchronously(context, cancellationToken)
            : SuspendAsync(context, cancellationToken);
    }

    ValueTask<int> SuspendSynchronously(
        PauseContext context,
        CancellationToken cancellationToken
    ) => new(SynchronousDebugPause.Run(token => SuspendAsync(context, token), cancellationToken));

    void ILuaDebugSession.ExecutionEnded()
    {
        lock (gate)
        {
            // A pause belongs to the execution it was requested against. If that execution unwound
            // (cancellation, host error) before a checkpoint consumed it, keeping the request would
            // stop the next, unrelated execution. A step that owns a still-suspended coroutine is
            // kept: its Lua frames are live even though no execution is running.
            var changed = pauseRequested;
            pauseRequested = false;
            if (step is { } request && request.State.CallStackFrameCount == 0)
            {
                step = null;
                changed = true;
            }
            if (changed)
                UpdateExecutionCodes();
        }
    }

    static bool ShouldStep(LuaState state, StepRequest request)
    {
        if (request.Mode == LuaDebugStep.Into)
            return true;
        if (state != request.State)
            return request.State.GetStatus() is LuaThreadStatus.Suspended or LuaThreadStatus.Dead;
        var depth = state.CallStackFrameCount;
        return depth < request.Depth
            || request.Mode == LuaDebugStep.Over
                && depth == request.Depth
                && state.CurrentFrameVersion == request.FrameId;
    }

    async ValueTask<int> SuspendAsync(PauseContext context, CancellationToken cancellationToken)
    {
        using var cancellation = cancellationToken.Register(Wake);
        try
        {
            RaiseStopped(context.Stop);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Command? command = null;
                Task wait;
                lock (gate)
                {
                    if (resumeRequested || !IsAttached)
                        break;
                    if (commands.Count != 0)
                        command = commands.Dequeue();
                    if (signal.Task.IsCompleted)
                        signal = NewSignal();
                    wait = signal.Task;
                }
                if (command != null)
                    await command.Run(context, cancellationToken);
                else
                    await wait;
            }
            return 0;
        }
        finally
        {
            Action<int>? stopEnded;
            lock (gate)
            {
                paused = null;
                while (commands.Count != 0)
                    commands
                        .Dequeue()
                        .Fail(new InvalidOperationException("The debug stop has ended."));
                UpdateExecutionCodes();
                stopEnded = StopEnded;
                if (disposed)
                    StopEnded = null;
            }
            context.Inspections.Dispose();
            stopEnded?.Invoke(context.Stop.Id);
        }
    }

    void RaiseStopped(LuaDebugStop stop)
    {
        Volatile.Write(ref stoppedThreadId, Environment.CurrentManagedThreadId);
        try
        {
            Stopped?.Invoke(stop);
        }
        finally
        {
            Volatile.Write(ref stoppedThreadId, -1);
        }
    }

    void Wake()
    {
        lock (gate)
            signal.TrySetResult(true);
    }

    static void CancelInspections(CancellationTokenSource? inspections)
    {
        try
        {
            inspections?.Cancel();
        }
        catch (ObjectDisposedException) { }
    }

    PauseContext RequireStop(int id) =>
        paused is { } context && context.Stop.Id == id && !resumeRequested
            ? context
            : throw new InvalidOperationException("The debug stop is no longer active.");

    Task<T> InspectAsync<T>(
        int stopId,
        Func<PauseContext, CancellationToken, ValueTask<T>> action,
        CancellationToken cancellationToken
    )
    {
        lock (gate)
        {
            // The paused execution is still inside the Stopped callback, so the command pump has
            // not started yet. Queueing there would wait forever; fail with the reason instead.
            if (Environment.CurrentManagedThreadId == Volatile.Read(ref stoppedThreadId))
                throw new InvalidOperationException(
                    "Inspect after the Stopped handler returns. Inspections are serviced by the "
                        + "paused execution, which is still inside that callback."
                );
            RequireStop(stopId);
            var command = new Command<T>(action, cancellationToken);
            commands.Enqueue(command);
            signal.TrySetResult(true);
            return command.Completion.Task;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            Stopped = null;
            if (paused == null)
                StopEnded = null;
            BreakpointChanged = null;
        }
        try
        {
            Detach();
        }
        finally
        {
            lock (gate)
            {
                sources.Clear();
                sourcesById.Clear();
            }
        }
    }

    static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    sealed record StepRequest(LuaDebugStep Mode, LuaState State, int Depth, int FrameId);

    sealed record PauseContext(LuaState State, int Pc, int Depth, int FrameId, LuaDebugStop Stop)
    {
        public CancellationTokenSource Inspections { get; } = new();
    }

    abstract class Command
    {
        public abstract ValueTask Run(PauseContext context, CancellationToken cancellationToken);
        public abstract void Fail(Exception exception);
    }

    sealed class Command<T>(
        Func<PauseContext, CancellationToken, ValueTask<T>> action,
        CancellationToken requestToken
    ) : Command
    {
        public readonly TaskCompletionSource<T> Completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public override async ValueTask Run(
            PauseContext context,
            CancellationToken cancellationToken
        )
        {
            try
            {
                requestToken.ThrowIfCancellationRequested();
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    requestToken,
                    cancellationToken,
                    context.Inspections.Token
                );
                Completion.TrySetResult(await action(context, linked.Token));
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        public override void Fail(Exception exception)
        {
            if (exception is OperationCanceledException cancelled)
                Completion.TrySetCanceled(cancelled.CancellationToken);
            else
                Completion.TrySetException(exception);
        }
    }
}
