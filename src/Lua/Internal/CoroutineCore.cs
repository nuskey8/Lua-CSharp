using Lua.Internal.CompilerServices;
using Lua.Runtime;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;

namespace Lua.Internal;

class CoroutineCore(LuaState state, LuaFunction function, bool isProtectedMode) : IValueTaskSource<CoroutineCore.YieldContext>,
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

    ManualResetValueTaskSourceCore<ResumeContext> resume;
    ManualResetValueTaskSourceCore<YieldContext> yield;
    Traceback? traceback;

    public bool IsProtectedMode { get; private set; } = isProtectedMode;
    public LuaFunction Function { get; private set; } = function;
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

    void IValueTaskSource<YieldContext>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
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

    void IValueTaskSource<ResumeContext>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        resume.OnCompleted(continuation, state, token, flags);
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    internal async ValueTask<int> ResumeAsyncCore(LuaStack stack, int argCount, int returnBase, LuaState? baseThread, CancellationToken cancellationToken = default)
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
                    Volatile.Write(ref status, (byte)LuaThreadStatus.Running);

                    if (!isFirstCall)
                    {
                        yield.SetResult(new(stack, argCount));
                    }

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
                        throw new LuaRuntimeException(baseThread, "cannot resume non-suspended coroutine");
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

            CancellationTokenRegistration registration = default;
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.UnsafeRegister(static x =>
                {
                    var coroutine = (CoroutineCore)x!;
                    coroutine.yield.SetException(new OperationCanceledException());
                }, this);
            }

            try
            {
                if (isFirstCall)
                {
                    Thread.Stack.PushRange(stack.AsSpan()[^argCount..]);
                    // functionTask = Function.InvokeAsync(new() { Access = this.CurrentAccess, ArgumentCount = Stack.Count, ReturnFrameBase = 0 }, cancellationToken);
                    functionTask = Thread.RunAsync(Function, Thread.Stack.Count, cancellationToken);
                    Volatile.Write(ref isFirstCall, false);
                    if (!functionTask.IsCompleted)
                    {
                        functionTask.GetAwaiter().OnCompleted(() => resume.SetResult(default));
                    }
                }

                ResumeContext result0;
                if (functionTask.IsCompleted || (result0 = await resumeTask).IsDead)
                {
                    _ = functionTask.Result;
                    Volatile.Write(ref status, (byte)LuaThreadStatus.Dead);
                    stack.PopUntil(returnBase);
                    stack.Push(true);
                    stack.PushRange(Thread.Stack.AsSpan());
                    Thread.Dispose();
                    return stack.Count - returnBase;
                }
                else
                {
                    Volatile.Write(ref status, (byte)LuaThreadStatus.Suspended);
                    var results = result0.Results;
                    stack.PopUntil(returnBase);
                    stack.Push(true);
                    stack.PushRange(results);
                    return results.Length + 1;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (IsProtectedMode)
                {
                    if (ex is ILuaTracebackBuildable tracebackBuildable)
                    {
                        traceback = tracebackBuildable.BuildOrGet();
                    }

                    Volatile.Write(ref status, (byte)LuaThreadStatus.Dead);
                    Thread.Dispose();
                    stack.PopUntil(returnBase);
                    stack.Push(false);
                    stack.Push(ex is LuaRuntimeException luaEx ? luaEx.ErrorObject : ex.Message);
                    return 2;
                }
                else
                {
                    throw;
                }
            }
            finally
            {
                registration.Dispose();
                resume.Reset();
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

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    internal async ValueTask<int> YieldAsyncCore(LuaStack stack, int argCount, int returnBase, LuaState? baseThread, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref status) != (byte)LuaThreadStatus.Running)
        {
            throw new LuaRuntimeException(baseThread, "cannot yield from a non-running coroutine");
        }

        if (baseThread != null)
        {
            if (baseThread.GetCallStackFrames()[^2].Function is not LuaClosure)
            {
                throw new LuaRuntimeException(baseThread, "attempt to yield across a C#-call boundary");
            }
        }

        resume.SetResult(new(stack, argCount));

        CancellationTokenRegistration registration = default;
        if (cancellationToken.CanBeCanceled)
        {
            registration = cancellationToken.UnsafeRegister(static x =>
            {
                var coroutine = (CoroutineCore)x!;
                coroutine.yield.SetException(new OperationCanceledException());
            }, this);
        }

    RETRY:
        try
        {
            var result = await new ValueTask<YieldContext>(this, yield.Version);
            stack.PopUntil(returnBase);
            stack.PushRange(result.Results);
            return result.Results.Length;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            yield.Reset();
            goto RETRY;
        }
        finally
        {
            registration.Dispose();
            yield.Reset();
        }
    }
}