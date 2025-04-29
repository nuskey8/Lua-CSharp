using System.Runtime.CompilerServices;
using Lua.Internal;
using Lua.Runtime;

namespace Lua;

public abstract class LuaThread
{
    internal LuaThread() { }

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
        throw new LuaRuntimeException(context.Thread.GetTraceback(), "attempt to yield from outside a coroutine");
    }

    internal class ThreadCoreData: IPoolNode<ThreadCoreData>
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
    public LuaState State { get;protected set; }

    internal ThreadCoreData? CoreData = new();
   
    internal BitFlags2 LineAndCountHookMask;
    internal BitFlags2 CallOrReturnHookMask;
    internal bool IsInHook;
    internal int HookCount;
    internal int BaseHookCount;
    internal int LastPc;
    internal LuaFunction? Hook { get; set; }
   
    internal LuaStack Stack => CoreData!.Stack;
    internal ref FastStackCore<CallStackFrame> CallStack => ref CoreData!.CallStack;

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

 


    public void Push(LuaValue value)
    {
        CoreData.Stack.Push(value);
    }

    public void Push(params ReadOnlySpan<LuaValue> span)
    {
        CoreData.Stack.PushRange(span);
    }

    public void Pop(int count)
    {
        CoreData.Stack.Pop(count);
    }

    public LuaValue Pop()
    {
        return CoreData.Stack.Pop();
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

    public Traceback GetTraceback()
    {
        var frames = GetCallStackFrames();

        return new(State) { RootFunc = frames[0].Function, StackFrames = GetCallStackFrames()[1..].ToArray() };
    }


    static void ThrowForEmptyStack() => throw new InvalidOperationException("Empty stack");
}