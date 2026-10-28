using System.Runtime.CompilerServices;
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

    readonly struct ResumeContext(LuaStack? stack, int argCount)
    {
        public ReadOnlySpan<LuaValue> Results => stack!.AsSpan()[^argCount..];
        public bool IsDead => stack == null;
    }

    internal byte status = (byte)LuaThreadStatus.Suspended;
    bool isFirstCall = true;
    ValueTask<int> functionTask;
    CancellationToken resumeCancellationToken;
    int yieldCompleted;

    const int ResumeIdle = 0;
    const int ResumeWaiting = 1;
    const int ResumeYielded = 2;
    const int ResumeCompleted = 3;
    int resumePhase;

    ManualResetValueTaskSourceCore<ResumeContext> resume;
    ManualResetValueTaskSourceCore<YieldContext> yield;
    Traceback? traceback;

    public bool IsProtectedMode { get; private set; } = isProtectedMode;
    public LuaFunction? Function { get; private set; } = function;
    public LuaState Thread { get; private set; } = state;
    public Traceback? Traceback => traceback;

    YieldContext IValueTaskSource<YieldContext>.GetResult(short token)
    {
        return yield.GetResult(token);
    }

    ValueTaskSourceStatus IValueTaskSource<YieldContext>.GetStatus(short token)
    {
        return yield.GetStatus(token);
    }

    void IValueTaskSource<YieldContext>.OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags
    )
    {
        // Avoid SynchronizationContext capture (deadlocks Unity WebGL sync-over-async).
        flags &= ~ValueTaskSourceOnCompletedFlags.UseSchedulingContext;
        yield.OnCompleted(continuation, state, token, flags);
    }

    ResumeContext IValueTaskSource<ResumeContext>.GetResult(short token)
    {
        return resume.GetResult(token);
    }

    ValueTaskSourceStatus IValueTaskSource<ResumeContext>.GetStatus(short token)
    {
        return resume.GetStatus(token);
    }

    void IValueTaskSource<ResumeContext>.OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags
    )
    {
        flags &= ~ValueTaskSourceOnCompletedFlags.UseSchedulingContext;
        resume.OnCompleted(continuation, state, token, flags);
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    internal async ValueTask<int> ResumeAsyncCore(
        LuaStack stack,
        int argCount,
        int returnBase,
        LuaState? baseThread,
        CancellationToken cancellationToken = default
    )
    {
        if (baseThread != null)
        {
            baseThread.UnsafeSetStatus(LuaThreadStatus.Normal);

            baseThread.GlobalState.ThreadStack.Push(Thread);
        }

        try
        {
            switch ((LuaThreadStatus)Volatile.Read(ref status))
            {
                case LuaThreadStatus.Suspended:
                    var phase = Interlocked.CompareExchange(
                        ref resumePhase,
                        ResumeWaiting,
                        ResumeIdle
                    );
                    if (phase == ResumeCompleted)
                        goto case LuaThreadStatus.Dead;
                    if (phase != ResumeIdle)
                        goto case LuaThreadStatus.Running;
                    Volatile.Write(ref status, (byte)LuaThreadStatus.Running);

                    break;
                case LuaThreadStatus.Normal:
                case LuaThreadStatus.Running:
                    if (IsProtectedMode)
                    {
                        stack.PopUntil(returnBase);
                        stack.Push(false);
                        stack.Push("cannot resume non-suspended coroutine");
                        return 2;
                    }
                    else
                    {
                        throw new LuaRuntimeException(
                            baseThread,
                            "cannot resume non-suspended coroutine"
                        );
                    }
                case LuaThreadStatus.Dead:
                    if (IsProtectedMode)
                    {
                        stack.PopUntil(returnBase);
                        stack.Push(false);
                        stack.Push("cannot resume dead coroutine");
                        return 2;
                    }
                    else
                    {
                        throw new LuaRuntimeException(baseThread, "cannot resume dead coroutine");
                    }
            }

            ValueTask<ResumeContext> resumeTask = new(this, resume.Version);
            resumeCancellationToken = cancellationToken;

            try
            {
                if (isFirstCall)
                {
                    Thread.Stack.PushRange(stack.AsSpan()[^argCount..]);
                    functionTask = Thread.RunAsync(
                        Function!,
                        Thread.Stack.Count,
                        cancellationToken
                    );
                    Volatile.Write(ref isFirstCall, false);
                    if (!functionTask.IsCompleted)
                    {
                        // UnsafeOnCompleted: OnCompleted would capture SyncContext and deadlock on WebGL.
                        functionTask.GetAwaiter().UnsafeOnCompleted(OnFunctionCompleted);
                    }
                }
                else
                {
                    if (Interlocked.CompareExchange(ref yieldCompleted, 1, 0) == 0)
                        yield.SetResult(new(stack, argCount));
                }

                ResumeContext result0;
                if (functionTask.IsCompleted || (result0 = await resumeTask).IsDead)
                {
                    _ = functionTask.Result;
                    functionTask = default;
                    resumeCancellationToken = default;
                    stack.PopUntil(returnBase);
                    stack.Push(true);
                    stack.PushRange(Thread.Stack.AsSpan());
                    Thread.Dispose();
                    Function = null;
                    Volatile.Write(ref status, (byte)LuaThreadStatus.Dead);
                    return stack.Count - returnBase;
                }
                else
                {
                    var results = result0.Results;
                    stack.PopUntil(returnBase);
                    stack.Push(true);
                    stack.PushRange(results);
                    Volatile.Write(ref status, (byte)LuaThreadStatus.Suspended);
                    return results.Length + 1;
                }
            }
            catch (Exception ex)
            {
                functionTask = default;
                resumeCancellationToken = default;
                if (ex is ILuaTracebackBuildable tracebackBuildable)
                {
                    traceback = tracebackBuildable.BuildOrGet();
                }

                Thread.Dispose();
                Function = null;
                Volatile.Write(ref status, (byte)LuaThreadStatus.Dead);
                if (!IsProtectedMode || ex is OperationCanceledException)
                    throw;

                stack.PopUntil(returnBase);
                stack.Push(false);
                // Wrapped C# exceptions carry a message rather than a Lua error object.
                // Keep error(nil) intact when the exception originated in Lua.
                stack.Push(
                    ex is LuaRuntimeException { InnerException: null } luaEx
                        ? luaEx.ErrorObject
                        : ex.Message
                );
                return 2;
            }
            finally
            {
                resume.Reset();
                var phase = Interlocked.CompareExchange(ref resumePhase, ResumeIdle, ResumeYielded);
                if (phase == ResumeCompleted)
                    CompleteSuspendedFunction();
                else if (phase == ResumeWaiting)
                    Interlocked.CompareExchange(ref resumePhase, ResumeIdle, ResumeWaiting);
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

    void OnFunctionCompleted()
    {
        resumeCancellationToken = default;
        var phase = Interlocked.Exchange(ref resumePhase, ResumeCompleted);
        if (phase == ResumeWaiting)
            resume.SetResult(default);
        else if (phase == ResumeIdle)
            CompleteSuspendedFunction();
    }

    void CancelYield()
    {
        if (Interlocked.CompareExchange(ref yieldCompleted, 1, 0) == 0)
            yield.SetException(new OperationCanceledException());
    }

    void CompleteSuspendedFunction()
    {
        if (Volatile.Read(ref status) != (byte)LuaThreadStatus.Suspended)
            return;
        try
        {
            functionTask.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            if (ex is ILuaTracebackBuildable tracebackBuildable)
                traceback = tracebackBuildable.BuildOrGet();
        }
        finally
        {
            functionTask = default;
            Thread.Dispose();
            Function = null;
            Volatile.Write(ref status, (byte)LuaThreadStatus.Dead);
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
        {
            throw new LuaRuntimeException(baseThread, "cannot yield from a non-running coroutine");
        }

        if (baseThread != null)
        {
            var frames = baseThread.GetCallStackFrames();
            if (frames.Length > 1 && frames[^2].Function is not LuaClosure)
            {
                throw new LuaRuntimeException(
                    baseThread,
                    "attempt to yield across a C#-call boundary"
                );
            }
        }

        CancellationTokenSource? linkedSource = null;
        var resumeToken = resumeCancellationToken;
        if (resumeToken.CanBeCanceled && resumeToken != cancellationToken)
        {
            if (cancellationToken.CanBeCanceled)
            {
                linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    resumeToken
                );
                cancellationToken = linkedSource.Token;
            }
            else
                cancellationToken = resumeToken;
        }

        CancellationTokenRegistration registration = default;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Yielded values remain owned by the resumer until its copy completes.
            Volatile.Write(ref resumePhase, ResumeYielded);
            resume.SetResult(new(stack, argCount));
            if (cancellationToken.CanBeCanceled)
                registration = cancellationToken.UnsafeRegister(
                    static x => ((CoroutineCore)x!).CancelYield(),
                    this
                );
            var result = await new ValueTask<YieldContext>(this, yield.Version);
            resumeCancellationToken.ThrowIfCancellationRequested();
            stack.PopUntil(returnBase);
            stack.PushRange(result.Results);
            return result.Results.Length;
        }
        finally
        {
            registration.Dispose();
            linkedSource?.Dispose();
            yield.Reset();
            Volatile.Write(ref yieldCompleted, 0);
        }
    }
}
