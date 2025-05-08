using System.Runtime.CompilerServices;
using Lua.Internal;
using Lua.Runtime;

namespace Lua;

public abstract class LuaThread
{
    protected LuaThread() { }

    public virtual LuaThreadStatus GetStatus()
    {
        return LuaThreadStatus.Running;
    }

    public virtual void UnsafeSetStatus(LuaThreadStatus status) { }

    public virtual ValueTask<int> ResumeAsync(LuaFunctionExecutionContext context, CancellationToken cancellationToken = default)
    {
        return new(context.Return(false, "cannot resume non-suspended coroutine"));
    }

    public virtual ValueTask<int> YieldAsync(LuaFunctionExecutionContext context, CancellationToken cancellationToken = default)
    {
        throw new LuaRuntimeException(context.Thread, "attempt to yield from outside a coroutine");
    }

    protected class ThreadCoreData : IPoolNode<ThreadCoreData>
    {
        //internal  LuaCoroutineData? coroutineData;
        internal LuaStack Stack = new();
        internal FastStackCore<CallStackFrame> CallStack;

        public void Clear()
        {
            Stack.Clear();
            CallStack.Clear();
        }

        static LinkedPool<ThreadCoreData> pool;
        ThreadCoreData? nextNode;
        public ref ThreadCoreData? NextNode => ref nextNode;

        public static ThreadCoreData Create()
        {
            if (!pool.TryPop(out ThreadCoreData result))
            {
                result = new ThreadCoreData();
            }

            return result;
        }

        public void Release()
        {
            Clear();
            pool.TryPush(this);
        }
    }

    public LuaState State { get; protected set; } = null!;
    protected ThreadCoreData? CoreData = new();
    internal BitFlags2 LineAndCountHookMask;
    internal BitFlags2 CallOrReturnHookMask;
    internal bool IsInHook;
    internal int HookCount;
    internal int BaseHookCount;
    internal int LastPc;

    internal LuaRuntimeException? CurrentException;
    internal readonly ReversedStack<CallStackFrame> ExceptionTrace = new();

    public bool IsRunning { get; protected set; }
    internal LuaFunction? Hook { get; set; }
    public LuaStack Stack => CoreData!.Stack;

    internal bool IsLineHookEnabled
    {
        get => LineAndCountHookMask.Flag0;
        set => LineAndCountHookMask.Flag0 = value;
    }

    internal bool IsCountHookEnabled
    {
        get => LineAndCountHookMask.Flag1;
        set => LineAndCountHookMask.Flag1 = value;
    }


    internal bool IsCallHookEnabled
    {
        get => CallOrReturnHookMask.Flag0;
        set => CallOrReturnHookMask.Flag0 = value;
    }

    internal bool IsReturnHookEnabled
    {
        get => CallOrReturnHookMask.Flag1;
        set => CallOrReturnHookMask.Flag1 = value;
    }

    public async ValueTask<int> RunAsync(LuaClosure closure, CancellationToken cancellationToken = default)
    {
        ThrowIfRunning();

        IsRunning = true;
        try
        {
            await closure.InvokeAsync(new() { Thread = this, ArgumentCount = Stack.Count, ReturnFrameBase = 0, }, cancellationToken);

            return Stack.Count;
        }
        finally
        {
            PopCallStackFrameUntil(0);
            IsRunning = false;
        }
    }

    public int CallStackFrameCount => CoreData == null ? 0 : CoreData!.CallStack.Count;

    public ref readonly CallStackFrame GetCurrentFrame()
    {
        return ref CoreData!.CallStack.PeekRef();
    }

    public ReadOnlySpan<LuaValue> GetStackValues()
    {
        return CoreData == null ? default : CoreData!.Stack.AsSpan();
    }

    public ReadOnlySpan<CallStackFrame> GetCallStackFrames()
    {
        return CoreData == null ? default : CoreData!.CallStack.AsSpan();
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PushCallStackFrame(in CallStackFrame frame)
    {
        CurrentException?.Build();
        CurrentException = null;
        CoreData!.CallStack.Push(frame);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PopCallStackFrameWithStackPop()
    {
        var coreData = CoreData!;
        var popFrame = coreData.CallStack.Pop();
        if (CurrentException != null)
        {
            ExceptionTrace.Push(popFrame);
        }

        coreData.Stack.PopUntil(popFrame.Base);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PopCallStackFrameWithStackPop(int frameBase)
    {
        var coreData = CoreData!;
        var popFrame = coreData.CallStack.Pop();
        if (CurrentException != null)
        {
            ExceptionTrace.Push(popFrame);
        }

        {
            coreData.Stack.PopUntil(frameBase);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PopCallStackFrame()
    {
        var coreData = CoreData!;
        var popFrame = coreData.CallStack.Pop();
        if (CurrentException != null)
        {
            ExceptionTrace.Push(popFrame);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PopCallStackFrameUntil(int top)
    {
        var coreData = CoreData!;
        ref var callStack = ref coreData.CallStack;
        if (CurrentException != null)
        {
            ExceptionTrace.Push(callStack.AsSpan()[top..]);
        }

        callStack.PopUntil(top);
    }

    internal void DumpStackValues()
    {
        var span = GetStackValues();
        for (int i = 0; i < span.Length; i++)
        {
            Console.WriteLine($"LuaStack [{i}]\t{span[i]}");
        }
    }

    public Traceback GetTraceback()
    {
        return new(State, GetCallStackFrames());
    }

    protected void ThrowIfRunning()
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("the lua state is currently running");
        }
    }
}