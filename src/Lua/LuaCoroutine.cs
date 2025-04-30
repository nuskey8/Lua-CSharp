using System.Threading.Tasks.Sources;
using Lua.Internal;
using Lua.Runtime;

namespace Lua;

public sealed class LuaCoroutine : LuaThread, IValueTaskSource<LuaCoroutine.YieldContext>, IValueTaskSource<LuaCoroutine.ResumeContext>, IPoolNode<LuaCoroutine>
{
    static LinkedPool<LuaCoroutine> pool;
    LuaCoroutine? nextNode;
    ref LuaCoroutine? IPoolNode<LuaCoroutine>.NextNode => ref nextNode;

    public static LuaCoroutine Create(LuaThread parent, LuaFunction function, bool isProtectedMode)
    {
        if (!pool.TryPop(out LuaCoroutine result))
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

    readonly struct ResumeContext(LuaStack stack, int argCount)
    {
        public ReadOnlySpan<LuaValue> Results => stack.AsSpan()[^argCount..];
    }

    byte status;
    bool isFirstCall = true;
    ValueTask<int> functionTask;

    ManualResetValueTaskSourceCore<ResumeContext> resume;
    ManualResetValueTaskSourceCore<YieldContext> yield;
    Traceback? traceback;

    internal void Init(LuaThread parent, LuaFunction function, bool isProtectedMode)
    {
        CoreData = ThreadCoreData.Create();
        State = parent.State;
        IsProtectedMode = isProtectedMode;
        Function = function;
        IsRunning = false;
    }

    public override LuaThreadStatus GetStatus() => (LuaThreadStatus)status;

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
        return ResumeAsync(stack, stack.Count, 0, cancellationToken);
    }

    public async ValueTask<int> ResumeAsync(LuaStack stack, int argCount, int returnBase, CancellationToken cancellationToken = default)
    {
        if (isFirstCall)
        {
            ThrowIfRunning();
            IsRunning = true;
        }

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
                    throw new LuaException("cannot resume non-suspended coroutine");
                }
            case LuaThreadStatus.Dead:
                if (IsProtectedMode)
                {
                    stack.PopUntil(returnBase);
                    stack.Push(false);
                    stack.Push("cannot resume non-suspended coroutine");
                    return 2;
                }
                else
                {
                    throw new LuaException("cannot resume dead coroutine");
                }
        }

        var resumeTask = new ValueTask<ResumeContext>(this, resume.Version);

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
                functionTask = Function.InvokeAsync(new() { Thread = this, ArgumentCount = Stack.Count, ReturnFrameBase = 0 }, cancellationToken).Preserve();

                Volatile.Write(ref isFirstCall, false);
            }

            var (index, result0, result1) = await ValueTaskEx.WhenAny(resumeTask, functionTask!);

            if (index == 0)
            {
                var results = result0.Results;
                stack.PopUntil(returnBase);
                stack.Push(true);
                stack.PushRange(results);
                return results.Length + 1;
            }
            else
            {
                Volatile.Write(ref status, (byte)LuaThreadStatus.Dead);
                stack.PopUntil(returnBase);
                stack.Push(true);
                stack.PushRange(Stack.AsSpan());
                ReleaseCore();
                return stack.Count - returnBase;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (IsProtectedMode)
            {
                traceback = (ex as LuaRuntimeException)?.LuaTraceback;
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

    public override async ValueTask<int> ResumeAsync(LuaFunctionExecutionContext context, CancellationToken cancellationToken = default)
    {
        var baseThread = context.Thread;
        baseThread.UnsafeSetStatus(LuaThreadStatus.Normal);

        context.State.ThreadStack.Push(this);
        try
        {
            switch ((LuaThreadStatus)Volatile.Read(ref status))
            {
                case LuaThreadStatus.Suspended:
                    Volatile.Write(ref status, (byte)LuaThreadStatus.Running);

                    if (!isFirstCall)
                    {
                        yield.SetResult(new(context.Thread.Stack, context.ArgumentCount));
                    }

                    break;
                case LuaThreadStatus.Normal:
                case LuaThreadStatus.Running:
                    if (IsProtectedMode)
                    {
                        return context.Return(false, "cannot resume non-suspended coroutine");
                    }
                    else
                    {
                        throw new LuaRuntimeException(context.Thread.GetTraceback(), "cannot resume non-suspended coroutine");
                    }
                case LuaThreadStatus.Dead:
                    if (IsProtectedMode)
                    {
                        return context.Return(false, "cannot resume dead coroutine");
                    }
                    else
                    {
                        throw new LuaRuntimeException(context.Thread.GetTraceback(), "cannot resume dead coroutine");
                    }
            }

            var resumeTask = new ValueTask<ResumeContext>(this, resume.Version);

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
                    Stack.PushRange(context.Arguments);
                    functionTask = Function.InvokeAsync(new() { Thread = this, ArgumentCount = Stack.Count, ReturnFrameBase = 0 }, cancellationToken).Preserve();

                    Volatile.Write(ref isFirstCall, false);
                }

                var (index, result0, result1) = await ValueTaskEx.WhenAny(resumeTask, functionTask!);

                if (index == 0)
                {
                    var results = result0.Results;
                    return context.Return(true, results);
                }
                else
                {
                    Volatile.Write(ref status, (byte)LuaThreadStatus.Dead);
                    var count = context.Return(true, Stack.AsSpan());
                    ReleaseCore();
                    return count;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (IsProtectedMode)
                {
                    traceback = (ex as LuaRuntimeException)?.LuaTraceback;
                    Volatile.Write(ref status, (byte)LuaThreadStatus.Dead);
                    ReleaseCore();
                    return context.Return(false, ex is LuaRuntimeException luaEx ? luaEx.ErrorObject : ex.Message);
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
            context.State.ThreadStack.Pop();
            baseThread.UnsafeSetStatus(LuaThreadStatus.Running);
        }
    }

    public override async ValueTask<int> YieldAsync(LuaFunctionExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref status) != (byte)LuaThreadStatus.Running)
        {
            throw new LuaRuntimeException(context.Thread.GetTraceback(), "cannot call yield on a coroutine that is not currently running");
        }

        if (context.Thread.GetCallStackFrames()[^2].Function is not LuaClosure)
        {
            throw new LuaRuntimeException(context.Thread.GetTraceback(), "attempt to yield across a C#-call boundary");
        }

        resume.SetResult(new(context.Thread.Stack, context.ArgumentCount));

        Volatile.Write(ref status, (byte)LuaThreadStatus.Suspended);

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
            return (context.Return(result.Results));
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