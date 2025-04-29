using System.Runtime.CompilerServices;
using Lua.Internal;
using Lua.Runtime;

namespace Lua;

public class LuaThread : IPoolNode<LuaThread>
{
    static LinkedPool<LuaThread> pool;
    LuaThread? parent;
    ref LuaThread? IPoolNode<LuaThread>.NextNode => ref parent;
    public static LuaThread Create(LuaState state)
    {
        var thread = new LuaThread { CoreData = { State = state } };
        return thread;
    }
    public virtual LuaThreadStatus GetStatus()
    {
        return LuaThreadStatus.Running;
    }

    public virtual void UnsafeSetStatus(LuaThreadStatus status){}
    public virtual ValueTask<int> ResumeAsync(LuaFunctionExecutionContext context, CancellationToken cancellationToken = default)
    {
        return new(context.Return(false, "cannot resume non-suspended coroutine"));
    }

    public virtual ValueTask<int> YieldAsync(LuaFunctionExecutionContext context, CancellationToken cancellationToken = default)
    {
        throw new LuaRuntimeException(context.State.GetTraceback(), "attempt to yield from outside a coroutine");
    }

    internal class ThreadCoreData
    {
        internal LuaState State;
        //internal  LuaCoroutineData? coroutineData;
        internal LuaStack Stack = new();
        internal FastStackCore<CallStackFrame> CallStack;
        internal BitFlags2 LineAndCountHookMask;
        internal BitFlags2 CallOrReturnHookMask;
        internal bool IsInHook;
        internal int HookCount;
        internal int BaseHookCount;
        internal int LastPc;
        internal LuaFunction? Hook { get; set; }
    }

    internal ThreadCoreData CoreData = new();
    
    public LuaState State=> CoreData.State;

    internal LuaStack Stack => CoreData.Stack;
    internal ref FastStackCore<CallStackFrame> CallStack => ref CoreData.CallStack;

    internal bool IsLineHookEnabled
    {
        get => CoreData.LineAndCountHookMask.Flag0;
        set => CoreData.LineAndCountHookMask.Flag0 = value;
    }

    internal bool IsCountHookEnabled
    {
        get => CoreData.LineAndCountHookMask.Flag1;
        set => CoreData.LineAndCountHookMask.Flag1 = value;
    }


    internal bool IsCallHookEnabled
    {
        get => CoreData.CallOrReturnHookMask.Flag0;
        set => CoreData.CallOrReturnHookMask.Flag0 = value;
    }

    internal bool IsReturnHookEnabled
    {
        get => CoreData.CallOrReturnHookMask.Flag1;
        set => CoreData.CallOrReturnHookMask.Flag1 = value;
    }

    internal BitFlags2 LineAndCountHookMask
    {
        get => CoreData.LineAndCountHookMask;
        set => CoreData.LineAndCountHookMask = value;
    }

    internal BitFlags2 CallOrReturnHookMask
    {
        get => CoreData.CallOrReturnHookMask;
        set => CoreData.CallOrReturnHookMask = value;
    }

    internal bool IsInHook
    {
        get => CoreData.IsInHook;
        set => CoreData.IsInHook = value;
    }

    internal int HookCount
    {
        get => CoreData.HookCount;
        set => CoreData.HookCount = value;
    }

    internal int BaseHookCount
    {
        get => CoreData.BaseHookCount;
        set => CoreData.BaseHookCount = value;
    }

    internal int LastPc
    {
        get => CoreData.LastPc;
        set => CoreData.LastPc = value;
    }

    internal LuaFunction? Hook
    {
        get => CoreData.Hook;
        set => CoreData.Hook = value;
    }

    public ref readonly CallStackFrame GetCurrentFrame()
    {
        return ref CoreData.CallStack.PeekRef();
    }

    public ReadOnlySpan<LuaValue> GetStackValues()
    {
        return CoreData.Stack.AsSpan();
    }

    public ReadOnlySpan<CallStackFrame> GetCallStackFrames()
    {
        return CoreData.CallStack.AsSpan();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PushCallStackFrame(in CallStackFrame frame)
    {
        CoreData.CallStack.Push(frame);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PopCallStackFrameWithStackPop()
    {
        if (CoreData.CallStack.TryPop(out var frame))
        {
            CoreData.Stack.PopUntil(frame.Base);
        }
        else
        {
            ThrowForEmptyStack();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PopCallStackFrameWithStackPop(int frameBase)
    {
        if (CoreData.CallStack.TryPop())
        {
            CoreData.Stack.PopUntil(frameBase);
        }
        else
        {
            ThrowForEmptyStack();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PopCallStackFrame()
    {
        if (!CoreData.CallStack.TryPop())
        {
            ThrowForEmptyStack();
        }
    }

    internal void DumpStackValues()
    {
        var span = GetStackValues();
        for (int i = 0; i < span.Length; i++)
        {
            Console.WriteLine($"LuaStack [{i}]\t{span[i]}");
        }
    }

    public void Release()
    {
        if (CoreData.CallStack.Count != 0)
        {
            throw new InvalidOperationException("This thread is running! Call stack is not empty!!");
        }
    }

    static void ThrowForEmptyStack() => throw new InvalidOperationException("Empty stack");
}