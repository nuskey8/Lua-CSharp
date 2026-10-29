using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Sources;
using Lua.Internal.CompilerServices;
using Lua.Runtime;

namespace Lua.Internal;

class CoroutineCore(LuaState state, LuaFunction function, bool isProtectedMode)
    : IValueTaskSource<CoroutineCore.YieldContext>,
        IValueTaskSource<CoroutineCore.ResumeContext>
{
    readonly struct YieldContext(LuaStack stack, int argCount)
    {
        public ReadOnlySpan<LuaValue> Results => stack.AsSpan()[^argCount..];
    }

    readonly struct ResumeContext(LuaStack stack, int argCount)
    {
        readonly int resultBase = stack.Count - argCount;
        public ReadOnlySpan<LuaValue> Results => stack.GetBuffer().Slice(resultBase, argCount);
        public bool IsDead => stack == null;

        public void ClearResults() => stack.PopUntil(resultBase);
    }

    const int Suspended = 0;
    const int Preparing = 1;
    const int Running = 2;
    const int Copying = 3;
    const int Cancelling = 4;
    const int Completed = 5;
    const int Closed = 6;
    const int CancelPending = 8;

    // Preparing and Copying give the resumer exclusive access to the stack range.
    // Cancellation waits until that ownership is released before waking the yield.
    int handoff;
    internal byte status = (byte)LuaThreadStatus.Suspended;
    bool isFirstCall = true;
    ValueTask<int> functionTask;
    LuaStack? yieldStack;
    int yieldReturnBase;
    CancellationTokenSource? executionSource;
    CancellationToken executionToken;
    CancellationScope? cancellation;
    ManualResetValueTaskSourceCore<ResumeContext> resume;
    ManualResetValueTaskSourceCore<YieldContext> yield;
    Traceback? traceback;

    public bool IsProtectedMode { get; } = isProtectedMode;
    public LuaFunction? Function { get; private set; } = function;
    public LuaState Thread { get; } = state;
    public Traceback? Traceback => traceback;

    internal CancellationToken GetCancellationOrigin(CancellationToken token) =>
        cancellation == null ? token : cancellation.GetOrigin(token);

    YieldContext IValueTaskSource<YieldContext>.GetResult(short token) => yield.GetResult(token);

    ValueTaskSourceStatus IValueTaskSource<YieldContext>.GetStatus(short token) =>
        yield.GetStatus(token);

    // Coroutine continuations must not require a SynchronizationContext (Unity WebGL).
    void IValueTaskSource<YieldContext>.OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags
    ) =>
        yield.OnCompleted(
            continuation,
            state,
            token,
            flags & ~ValueTaskSourceOnCompletedFlags.UseSchedulingContext
        );

    ResumeContext IValueTaskSource<ResumeContext>.GetResult(short token) => resume.GetResult(token);

    ValueTaskSourceStatus IValueTaskSource<ResumeContext>.GetStatus(short token) =>
        resume.GetStatus(token);

    void IValueTaskSource<ResumeContext>.OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags
    ) =>
        resume.OnCompleted(
            continuation,
            state,
            token,
            flags & ~ValueTaskSourceOnCompletedFlags.UseSchedulingContext
        );

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    internal async ValueTask<int> ResumeAsyncCore(
        LuaStack stack,
        int argCount,
        int returnBase,
        LuaState? baseThread,
        CancellationToken cancellationToken = default,
        bool includeStatus = true
    )
    {
        if (baseThread != null)
        {
            baseThread.UnsafeSetStatus(LuaThreadStatus.Normal);
            baseThread.GlobalState.ThreadStack.Push(Thread);
        }
        try
        {
            var currentStatus = (LuaThreadStatus)Volatile.Read(ref status);
            if (currentStatus != LuaThreadStatus.Suspended)
                return ResumeError(
                    stack,
                    returnBase,
                    baseThread,
                    currentStatus == LuaThreadStatus.Dead
                        ? "cannot resume dead coroutine"
                        : "cannot resume non-suspended coroutine",
                    includeStatus
                );

            var phase = Interlocked.CompareExchange(ref handoff, Preparing, Suspended);
            if (phase != Suspended)
                return ResumeError(
                    stack,
                    returnBase,
                    baseThread,
                    phase is Completed or Closed or Cancelling
                        ? "cannot resume dead coroutine"
                        : "cannot resume non-suspended coroutine",
                    includeStatus
                );
            try
            {
                if (isFirstCall)
                    Thread.Stack.EnsureCapacity(Thread.Stack.Count + argCount);
                else
                    yieldStack!.EnsureCapacity(yieldReturnBase + argCount);
                if (executionSource == null)
                {
                    // A callback can retain its execution token across multiple resumes.
                    executionSource = new();
                    executionToken = executionSource.Token;
                }
                SetResumeToken(cancellationToken);
                if (isFirstCall)
                    Thread.Stack.PushRange(stack.AsSpan()[^argCount..]);
            }
            catch (Exception error)
            {
                ReleaseTransfer(Preparing);
                if (error is LuaStackOverflowException)
                    return ResumeError(
                        stack,
                        returnBase,
                        baseThread,
                        "too many arguments to resume",
                        includeStatus
                    );
                throw;
            }

            var resumeTask = new ValueTask<ResumeContext>(this, resume.Version);
            Volatile.Write(ref status, (byte)LuaThreadStatus.Running);
            var cancelled =
                Interlocked.CompareExchange(ref handoff, Running, Preparing)
                == (Preparing | CancelPending);
            if (cancelled)
                Interlocked.Exchange(ref handoff, Running | CancelPending);
            try
            {
                Exception? cancellationFailure = null;
                if (isFirstCall)
                {
                    if (cancelled)
                        executionSource!.Cancel();
                    isFirstCall = false;
                    functionTask = Thread.RunAsync(Function!, Thread.Stack.Count, executionToken);
                    if (functionTask.IsCompleted)
                        OnFunctionCompleted();
                    else
                        functionTask.GetAwaiter().UnsafeOnCompleted(OnFunctionCompleted);
                }
                else if (cancelled)
                {
                    var error = new OperationCanceledException(
                        GetCancellationOrigin(executionToken)
                    );
                    try
                    {
                        executionSource!.Cancel();
                    }
                    catch (Exception failure)
                    {
                        cancellationFailure = failure;
                    }
                    yield.SetException(error);
                }
                else
                    yield.SetResult(new(stack, argCount));

                ResumeContext result;
                try
                {
                    result = await resumeTask;
                }
                catch (Exception error)
                {
                    if (cancellationFailure != null)
                        ExceptionDispatchInfo.Capture(cancellationFailure).Throw();
                    if (!IsProtectedMode || error is OperationCanceledException)
                        throw;
                    stack.PopUntil(returnBase);
                    var value = error is LuaRuntimeException { InnerException: null } luaError
                        ? luaError.ErrorObject
                        : new LuaValue(error.Message);
                    if (includeStatus)
                        stack.PushRange(false, value);
                    else
                        stack.Push(value);
                    return includeStatus ? 2 : 1;
                }
                try
                {
                    var results = result.IsDead ? Thread.Stack.AsSpan() : result.Results;
                    stack.PopUntil(returnBase);
                    if (includeStatus)
                        stack.PushRange(true, results);
                    else
                    {
                        // Lua 5.2 reserves one spare slot for wrapped results too.
                        stack.EnsureCapacity(returnBase + results.Length + 1);
                        results.CopyTo(stack.GetBuffer()[returnBase..]);
                        stack.NotifyTop(returnBase + results.Length);
                    }
                    return results.Length + (includeStatus ? 1 : 0);
                }
                catch (LuaStackOverflowException)
                {
                    return ResumeError(
                        stack,
                        returnBase,
                        baseThread,
                        "too many results to resume",
                        includeStatus
                    );
                }
                finally
                {
                    if (!result.IsDead)
                        result.ClearResults();
                }
            }
            finally
            {
                resume.Reset();
                if (Volatile.Read(ref handoff) == Completed)
                    FinishExecution();
                else
                    ReleaseTransfer(Copying);
            }
        }
        finally
        {
            if (baseThread != null)
            {
                baseThread.GlobalState.ThreadStack.Pop();
                baseThread.UnsafeSetStatus(LuaThreadStatus.Running);
            }
        }
    }

    int ResumeError(
        LuaStack stack,
        int returnBase,
        LuaState? baseThread,
        string message,
        bool includeStatus
    )
    {
        if (!IsProtectedMode)
            throw new LuaRuntimeException(baseThread, message);
        stack.PopUntil(returnBase);
        if (includeStatus)
            stack.PushRange(false, new LuaValue(message));
        else
            stack.Push(message);
        return includeStatus ? 2 : 1;
    }

    void OnFunctionCompleted()
    {
        Exception? error = null;
        try
        {
            _ = functionTask.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            error =
                exception is OperationCanceledException cancelled
                && exception is not LuaCanceledException
                    ? new LuaCanceledException(
                        Thread,
                        GetCancellationOrigin(cancelled.CancellationToken),
                        exception
                    )
                    : exception;
            if (error is ILuaTracebackBuildable buildable)
                traceback = buildable.BuildOrGet();
        }
        functionTask = default;
        var phase = Interlocked.Exchange(ref handoff, Completed);
        if (phase == Cancelling)
            FinishExecution();
        else if ((phase & ~CancelPending) == Running)
        {
            if (error != null)
                resume.SetException(error);
            else
                resume.SetResult(default);
        }
    }

    void ReleaseTransfer(int phase)
    {
        Volatile.Write(ref status, (byte)LuaThreadStatus.Suspended);
        if (Interlocked.CompareExchange(ref handoff, Suspended, phase) == (phase | CancelPending))
        {
            CompleteCancellation(phase | CancelPending);
        }
    }

    void RequestCancellation()
    {
        while (true)
        {
            var phase = Volatile.Read(ref handoff);
            var next = phase switch
            {
                Preparing or Copying => phase | CancelPending,
                Running => Running | CancelPending,
                Suspended => Copying | CancelPending,
                _ => phase,
            };
            if (next == phase)
                return;
            if (Interlocked.CompareExchange(ref handoff, next, phase) != phase)
                continue;
            if (phase == Suspended)
                CompleteCancellation(next);
            else if (phase == Running)
                executionSource!.Cancel();
            return;
        }
    }

    void CompleteCancellation(int phase)
    {
        var error = new OperationCanceledException(GetCancellationOrigin(executionToken));
        try
        {
            executionSource!.Cancel();
        }
        finally
        {
            yield.SetException(error);
            if (Interlocked.CompareExchange(ref handoff, Cancelling, phase) == Completed)
                FinishExecution();
        }
    }

    CancellationScope EnsureCancellation() => cancellation ??= new(this, executionSource!);

    void SetResumeToken(CancellationToken token)
    {
        if (token.CanBeCanceled && token != executionToken)
            EnsureCancellation();
        cancellation?.SetResumeToken(token, isFirstCall);
    }

    void FinishExecution()
    {
        Thread.Dispose();
        Function = null;
        cancellation?.Dispose();
        cancellation = null;
        executionSource?.Dispose();
        executionSource = null;
        executionToken = default;
        Volatile.Write(ref handoff, Closed);
        Volatile.Write(ref status, (byte)LuaThreadStatus.Dead);
    }

    sealed class CancellationScope : IDisposable
    {
        readonly CoroutineCore owner;
        CancellationToken lifetimeToken;
        CancellationToken resumeToken;
        internal CancellationToken YieldToken;
        CancellationTokenRegistration lifetimeRegistration;
        CancellationTokenRegistration resumeRegistration;

        internal CancellationScope(CoroutineCore owner, CancellationTokenSource source)
        {
            this.owner = owner;
            Token = source.Token;
        }

        internal CancellationToken Token { get; }

        internal CancellationToken GetOrigin(CancellationToken token) =>
            token != Token && token.IsCancellationRequested ? token
            : lifetimeToken.IsCancellationRequested ? lifetimeToken
            : resumeToken.IsCancellationRequested ? resumeToken
            : YieldToken.IsCancellationRequested ? YieldToken
            : token;

        internal void Cancel() => owner.RequestCancellation();

        internal void SetResumeToken(CancellationToken token, bool first)
        {
            resumeRegistration.Dispose();
            resumeRegistration = default;
            resumeToken = token;
            if (first)
            {
                lifetimeToken = token;
                if (token.CanBeCanceled)
                    lifetimeRegistration = Register(token);
            }
            else if (token.CanBeCanceled && token != lifetimeToken && token != Token)
                resumeRegistration = Register(token);
        }

        internal CancellationTokenRegistration Register(CancellationToken token) =>
            token.UnsafeRegister(static value => ((CancellationScope)value!).Cancel(), this);

        public void Dispose()
        {
            lifetimeRegistration.Dispose();
            resumeRegistration.Dispose();
            lifetimeRegistration = resumeRegistration = default;
            lifetimeToken = resumeToken = YieldToken = default;
        }
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    internal async ValueTask<int> YieldAsyncCore(
        LuaStack stack,
        int argCount,
        int returnBase,
        LuaState? baseThread,
        CancellationToken cancellationToken = default
    )
    {
        if (Volatile.Read(ref status) != (byte)LuaThreadStatus.Running)
            throw new LuaRuntimeException(baseThread, "cannot yield from a non-running coroutine");
        if (baseThread != null)
        {
            var frames = baseThread.GetCallStackFrames();
            if (frames.Length > 1 && frames[^2].Function is not LuaClosure)
                throw new LuaRuntimeException(
                    baseThread,
                    "attempt to yield across a C#-call boundary"
                );
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (cancellationToken.CanBeCanceled && cancellationToken != executionToken)
            EnsureCancellation();
        Thread.ThrowIfCancellationRequested(executionToken);
        CancellationTokenRegistration registration = default;
        var published = false;
        if (Interlocked.CompareExchange(ref handoff, Copying, Running) != Running)
            throw new LuaCanceledException(Thread, GetCancellationOrigin(executionToken));
        try
        {
            Thread.SuspendUpValues();
            yieldStack = stack;
            yieldReturnBase = returnBase;
            if (cancellation != null)
                cancellation.YieldToken = cancellationToken;
            if (cancellationToken.CanBeCanceled && cancellationToken != executionToken)
                registration = cancellation!.Register(cancellationToken);
            published = true;
            resume.SetResult(new(stack, argCount));
            var result = await new ValueTask<YieldContext>(this, yield.Version);
            Thread.ThrowIfCancellationRequested(executionToken);
            Thread.ResumeUpValues();
            stack.PopUntil(returnBase);
            stack.PushRange(result.Results);
            return result.Results.Length;
        }
        catch
        {
            if (!published)
            {
                Thread.ResumeUpValues();
                if (Interlocked.Exchange(ref handoff, Running) == (Copying | CancelPending))
                    RequestCancellation();
            }
            throw;
        }
        finally
        {
            registration.Dispose();
            yieldStack = null;
            if (cancellation != null)
                cancellation.YieldToken = default;
            yield.Reset();
        }
    }
}
