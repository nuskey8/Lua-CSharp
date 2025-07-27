using System.Threading.Tasks.Sources;
using Lua.Internal;
using Lua.Internal.CompilerServices;
using Lua.Runtime;
using System.Runtime.CompilerServices;

namespace Lua;

public sealed class LuaCoroutine : LuaState, IValueTaskSource<LuaCoroutine.YieldContext>, IValueTaskSource<LuaCoroutine.ResumeContext>, IPoolNode<LuaCoroutine>
{
    static LinkedPool<LuaCoroutine> pool;
    LuaCoroutine? nextNode;

    ref LuaCoroutine? IPoolNode<LuaCoroutine>.NextNode => ref nextNode;

    public static LuaCoroutine Create(LuaState parent, LuaFunction function, bool isProtectedMode)
    {
        if (!pool.TryPop(out var result))
        {
            result = new();
        }

        result.Init(parent, function, isProtectedMode);
        return result;
    }

    public void Release()
    {
        if (CoreData != null && CoreData.CallStack.Count != 0)
        {
            throw new InvalidOperationException("This thread is running! Call stack is not empty!!");
        }

        ReleaseCore();
        pool.TryPush(this);
    }

    readonly struct YieldContext(LuaStack stack, int argCount)
    {
        public ReadOnlySpan<LuaValue> Results => stack.AsSpan()[^argCount..];
    }

    readonly struct ResumeContext(LuaStack? stack, int argCount)
    {
        public ReadOnlySpan<LuaValue> Results => stack!.AsSpan()[^argCount..];

        public bool IsDead => stack == null;
    }

    byte status;
    bool isFirstCall = true;
    ValueTask<int> functionTask;

    ManualResetValueTaskSourceCore<ResumeContext> resume;
    ManualResetValueTaskSourceCore<YieldContext> yield;
    Traceback? traceback;

    internal void Init(LuaState parent, LuaFunction function, bool isProtectedMode)
    {
        CoreData = ThreadCoreData.Create();
        GlobalState = parent.GlobalState;
        IsProtectedMode = isProtectedMode;
        Function = function;
    }

    public override LuaThreadStatus GetStatus()
    {
        return (LuaThreadStatus)status;
    }

    public override void UnsafeSetStatus(LuaThreadStatus status)
    {
        this.status = (byte)status;
    }

    public bool IsProtectedMode { get; private set; }
    public LuaFunction Function { get; private set; } = null!;

    internal Traceback? LuaTraceback => traceback;

    public bool CanResume => status == (byte)LuaThreadStatus.Suspended;

    public ValueTask<int> ResumeAsync(LuaStack stack, CancellationToken cancellationToken = default)
    {
        return ResumeAsyncCore(stack, stack.Count, 0, null, cancellationToken);
    }

    public override ValueTask<int> ResumeAsync(LuaFunctionExecutionContext context, CancellationToken cancellationToken = default)
    {
        return ResumeAsyncCore(context.State.Stack, context.ArgumentCount, context.ReturnFrameBase, context.State, cancellationToken);
    }


    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    async ValueTask<int> ResumeAsyncCore(LuaStack stack, int argCount, int returnBase, LuaState? baseThread, CancellationToken cancellationToken = default)
    {
        if (baseThread != null)
        {
            baseThread.UnsafeSetStatus(LuaThreadStatus.Normal);

            baseThread.GlobalState.ThreadStack.Push(this);
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
                    var coroutine = (LuaCoroutine)x!;
                    coroutine.yield.SetException(new OperationCanceledException());
                }, this);
            }

            try
            {
                if (isFirstCall)
                {
                    Stack.PushRange(stack.AsSpan()[^argCount..]);
                    //functionTask = Function.InvokeAsync(new() { Access = this.CurrentAccess, ArgumentCount = Stack.Count, ReturnFrameBase = 0 }, cancellationToken);
                    functionTask = CurrentAccess.RunAsync(Function, Stack.Count, cancellationToken);
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
                    stack.PushRange(Stack.AsSpan());
                    ReleaseCore();
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
                    ReleaseCore();
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

    public ValueTask<int> YieldAsync(LuaStack stack, CancellationToken cancellationToken = default)
    {
        return YieldAsyncCore(stack, stack.Count, 0, null, cancellationToken);
    }

    public override ValueTask<int> YieldAsync(LuaFunctionExecutionContext context, CancellationToken cancellationToken = default)
    {
        return YieldAsyncCore(context.State.Stack, context.ArgumentCount, context.ReturnFrameBase, context.State, cancellationToken);
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    async ValueTask<int> YieldAsyncCore(LuaStack stack, int argCount, int returnBase, LuaState? baseThread, CancellationToken cancellationToken = default)
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
                var coroutine = (LuaCoroutine)x!;
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

    void ReleaseCore()
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        CoreData?.Release();
        CoreData = null!;
    }
}