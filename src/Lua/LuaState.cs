using Lua.CodeAnalysis.Compilation;
using System.Runtime.CompilerServices;
using Lua.Internal;
using Lua.Platforms;
using Lua.Runtime;
using System.Buffers;

namespace Lua;

public class LuaState : IDisposable
{
    internal LuaState(LuaGlobalState globalState)
    {
        GlobalState = globalState;
        CoreData = ThreadCoreData.Create();
    }

    internal LuaState(LuaGlobalState globalState, LuaFunction function, bool isProtectedMode)
    {
        GlobalState = globalState;
        CoreData = ThreadCoreData.Create();
        coroutine = new(this, function, isProtectedMode);
    }

    public static LuaState Create()
    {
        var globalState = LuaGlobalState.Create();
        return globalState.MainThread;
    }

    public static LuaState Create(LuaPlatform platform)
    {
        return LuaGlobalState.Create(platform).MainThread;
    }

    internal static LuaState CreateCoroutine(LuaGlobalState globalState, LuaFunction function, bool isProtectedMode = false)
    {
        return new(globalState, function, isProtectedMode);
    }

    public LuaState CreateThread()
    {
        return new(GlobalState);
    }

    public LuaState CreateCoroutine(LuaFunction function, bool isProtectedMode = false)
    {
        return new(GlobalState, function, isProtectedMode);
    }


    public LuaThreadStatus GetStatus()
    {
        if (coroutine is not null)
        {
            return (LuaThreadStatus)coroutine.status;
        }

        return LuaThreadStatus.Running;
    }

    public void UnsafeSetStatus(LuaThreadStatus status)
    {
        if (coroutine is null)
        {
            return;
        }

        coroutine.status = (byte)status;
    }

    public ValueTask<int> ResumeAsync(LuaFunctionExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (coroutine is not null)
        {
            return coroutine.ResumeAsyncCore(context.State.Stack, context.ArgumentCount, context.ReturnFrameBase, context.State, cancellationToken);
        }

        return new(context.Return(false, "cannot resume non-suspended coroutine"));
    }

    public ValueTask<int> ResumeAsync(LuaStack stack, CancellationToken cancellationToken = default)
    {
        if (coroutine is not null)
        {
            return coroutine.ResumeAsyncCore(stack, stack.Count, 0, null, cancellationToken);
        }

        stack.Push(false);
        stack.Push("cannot resume non-suspended coroutine");
        return new(2);
    }

    public ValueTask<int> YieldAsync(LuaFunctionExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (coroutine is not null)
        {
            return coroutine.YieldAsyncCore(context.State.Stack, context.ArgumentCount, context.ReturnFrameBase, context.State, cancellationToken);
        }

        throw new LuaRuntimeException(context.State, "cannot yield from a non-running coroutine");
    }

    public ValueTask<int> YieldAsync(LuaStack stack, CancellationToken cancellationToken = default)
    {
        if (coroutine is not null)
        {
            return coroutine.YieldAsyncCore(stack, stack.Count, 0, null, cancellationToken);
        }

        throw new LuaRuntimeException(null, "cannot yield from a non-running coroutine");
    }

    class ThreadCoreData : IPoolNode<ThreadCoreData>
    {
        //internal  LuaCoroutineData? coroutineData;
        internal readonly LuaStack Stack = new();
        internal FastStackCore<CallStackFrame> CallStack;

        void Clear()
        {
            Stack.Clear();
            CallStack.Clear();
        }

        static LinkedPool<ThreadCoreData> pool;
        ThreadCoreData? nextNode;

        public ref ThreadCoreData? NextNode => ref nextNode;

        public static ThreadCoreData Create()
        {
            if (!pool.TryPop(out var result))
            {
                result = new();
            }

            return result;
        }

        public void Release()
        {
            Clear();
            pool.TryPush(this);
        }
    }


    FastListCore<UpValue> openUpValues;
    internal int CallCount;
    internal LuaGlobalState GlobalState { get; }
    ThreadCoreData? CoreData;
    CoroutineCore? coroutine;
    internal bool IsLineHookEnabled;
    internal BitFlags2 CallOrReturnHookMask;
    internal bool IsInHook;
    internal long HookCount;
    internal int BaseHookCount;
    internal int LastPc;

    internal ILuaTracebackBuildable? CurrentException;
    internal readonly ReversedStack<CallStackFrame> ExceptionTrace = new();
    internal LuaFunction? LastCallerFunction;

    internal ref FastListCore<UpValue> OpenUpValues => ref openUpValues;
    public bool IsRunning => CallStackFrameCount != 0;
    public bool IsCoroutine => coroutine != null;
    internal LuaFunction? Hook { get; set; }

    public LuaFunction? CoroutineFunction => coroutine?.Function;

    public bool CanResume => GetStatus() == LuaThreadStatus.Suspended;

    public LuaStack Stack => CoreData!.Stack;

    internal Traceback? LuaTraceback => coroutine?.Traceback;

    public LuaTable Environment => GlobalState.Environment;

    public LuaTable Registry => GlobalState.Registry;

    public LuaTable LoadedModules => GlobalState.LoadedModules;

    public LuaTable PreloadModules => GlobalState.PreloadModules;

    public LuaState MainThread => GlobalState.MainThread;

    public ILuaModuleLoader? ModuleLoader
    {
        get => GlobalState.ModuleLoader;
        set => GlobalState.ModuleLoader = value;
    }

    public LuaPlatform Platform
    {
        get => GlobalState.Platform;
        set => GlobalState.Platform = value;
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


    internal CallStackFrame CreateCallStackFrame(LuaFunction function, int argumentCount, int returnBase, int callerInstructionIndex)
    {
        var state = this;
        var varArgumentCount = function.GetVariableArgumentCount(argumentCount);
        if (varArgumentCount != 0)
        {
            if (varArgumentCount < 0)
            {
                state.Stack.SetTop(state.Stack.Count - varArgumentCount);
                argumentCount -= varArgumentCount;
                varArgumentCount = 0;
            }
            else
            {
                LuaVirtualMachine.PrepareVariableArgument(state.Stack, argumentCount, varArgumentCount);
            }
        }

        CallStackFrame frame = new()
        {
            Base = state.Stack.Count - argumentCount,
            VariableArgumentCount = varArgumentCount,
            Function = function,
            ReturnBase = returnBase,
            CallerInstructionIndex = callerInstructionIndex
        };

        if (state.IsInHook)
        {
            frame.Flags |= CallStackFrameFlags.InHook;
        }

        return frame;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PushCallStackFrame(in CallStackFrame frame)
    {
        CurrentException?.BuildOrGet();
        CurrentException = null;
        ref var callStack = ref CoreData!.CallStack;
        callStack.Push(frame);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PopCallStackFrameWithStackPop()
    {
        var coreData = CoreData!;
        ref var callStack = ref coreData.CallStack;
        var popFrame = callStack.Pop();
        if (CurrentException != null)
        {
            ExceptionTrace.Push(popFrame);
        }

        coreData.Stack.PopUntil(popFrame.ReturnBase);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PopCallStackFrameWithStackPop(int frameBase)
    {
        var coreData = CoreData!;
        ref var callStack = ref coreData.CallStack;
        var popFrame = callStack.Pop();
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
        ref var callStack = ref coreData.CallStack;
        var popFrame = callStack.Pop();
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

    public void SetHook(LuaFunction? hook, string mask, int count = 0)
    {
        if (hook is null)
        {
            HookCount = 0;
            BaseHookCount = 0;
            Hook = null;
            IsLineHookEnabled = false;
            IsCallHookEnabled = false;
            IsReturnHookEnabled = false;
            return;
        }

        HookCount = count > 0 ? count + 1 : 0;
        BaseHookCount = count;

        IsLineHookEnabled = mask.Contains('l');
        IsCallHookEnabled = mask.Contains('c');
        IsReturnHookEnabled = mask.Contains('r');

        if (IsLineHookEnabled)
        {
            LastPc = CallStackFrameCount > 0 ? GetCurrentFrame().CallerInstructionIndex : -1;
        }

        Hook = hook;
    }

    internal void DumpStackValues()
    {
        var span = GetStackValues();
        for (var i = 0; i < span.Length; i++)
        {
            Console.WriteLine($"LuaStack [{i}]\t{span[i]}");
        }
    }

    public Traceback GetTraceback()
    {
        return new(this, GetCallStackFrames());
    }

    public ValueTask<int> RunAsync(LuaFunction function, CancellationToken cancellationToken = default)
    {
        return RunAsync(function, 0, Stack.Count, cancellationToken);
    }

    public ValueTask<int> RunAsync(LuaFunction function, int argumentCount, CancellationToken cancellationToken = default)
    {
        return RunAsync(function, argumentCount, Stack.Count - argumentCount, cancellationToken);
    }

    public async ValueTask<int> RunAsync(LuaFunction function, int argumentCount, int returnBase, CancellationToken cancellationToken = default)
    {
        if (function == null)
        {
            throw new ArgumentNullException(nameof(function));
        }

        this.ThrowIfCancellationRequested(cancellationToken);
        var state = this;
        var varArgumentCount = function.GetVariableArgumentCount(argumentCount);
        if (varArgumentCount != 0)
        {
            if (varArgumentCount < 0)
            {
                state.Stack.SetTop(state.Stack.Count - varArgumentCount);
                varArgumentCount = 0;
            }
            else
            {
                LuaVirtualMachine.PrepareVariableArgument(state.Stack, argumentCount, varArgumentCount);
            }

            argumentCount -= varArgumentCount;
        }

        CallStackFrame frame = new() { Base = state.Stack.Count - argumentCount, VariableArgumentCount = varArgumentCount, Function = function, ReturnBase = returnBase };
        if (state.IsInHook)
        {
            frame.Flags |= CallStackFrameFlags.InHook;
        }

        state.PushCallStackFrame(frame);
        LuaFunctionExecutionContext context = new() { State = state, ArgumentCount = argumentCount, ReturnFrameBase = returnBase };
        var callStackTop = state.CallStackFrameCount;
        try
        {
            if (CallOrReturnHookMask.Value != 0 && !IsInHook)
            {
                return await LuaVirtualMachine.ExecuteCallHook(context, cancellationToken);
            }

            return await function.Func(context, cancellationToken);
        }
        finally
        {
            PopCallStackFrameUntil(callStackTop - 1);
        }
    }


    public unsafe LuaClosure Load(ReadOnlySpan<char> chunk, string chunkName, LuaTable? environment = null)
    {
        Prototype prototype;
        fixed (char* ptr = chunk)
        {
            prototype = Parser.Parse(this, new(ptr, chunk.Length), chunkName);
        }

        return new(this, prototype, environment);
    }

    public LuaClosure Load(ReadOnlySpan<byte> chunk, string? chunkName = null, string mode = "bt", LuaTable? environment = null)
    {
        if (chunk.Length > 4)
        {
            if (chunk[0] == '\e')
            {
                return new(this, Parser.UnDump(chunk, chunkName), environment);
            }
        }

        chunk = BomUtility.GetEncodingFromBytes(chunk, out var encoding);

        var charCount = encoding.GetCharCount(chunk);
        var pooled = ArrayPool<char>.Shared.Rent(charCount);
        try
        {
            var chars = pooled.AsSpan(0, charCount);
            encoding.GetChars(chunk, chars);
            chunkName ??= chars.ToString();

            return Load(chars, chunkName, environment);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(pooled);
        }
    }


    internal UpValue GetOrAddUpValue(int registerIndex)
    {
        foreach (var upValue in openUpValues.AsSpan())
        {
            if (upValue.RegisterIndex == registerIndex)
            {
                return upValue;
            }
        }

        var newUpValue = UpValue.Open(this, registerIndex);
        openUpValues.Add(newUpValue);
        return newUpValue;
    }

    internal void CloseUpValues(int frameBase)
    {
        for (var i = 0; i < openUpValues.Length; i++)
        {
            var upValue = openUpValues[i];

            if (upValue.RegisterIndex >= frameBase)
            {
                upValue.Close();
                openUpValues.RemoveAtSwapBack(i);
                i--;
            }
        }
    }

    public void Dispose()
    {
        if (CoreData!.CallStack.Count != 0)
        {
            throw new InvalidOperationException("This state is running! Call stack is not empty!!");
        }

        CoreData.Release();
        CoreData = null!;
    }
}