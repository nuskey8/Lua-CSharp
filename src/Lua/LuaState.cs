using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using Lua.CodeAnalysis.Compilation;
using Lua.Internal;
using Lua.Internal.CompilerServices;
using Lua.IO;
using Lua.Platforms;
using Lua.Runtime;

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

    /// <summary>Creates a thread sharing this state's globals and copying its host hook settings.</summary>
    public LuaState CreateThread()
    {
        var thread = new LuaState(GlobalState);
        thread.InheritHook(this);
        return thread;
    }

    /// <summary>Creates a coroutine sharing this state's globals and copying its host hook settings.</summary>
    public LuaState CreateCoroutine(LuaFunction function, bool isProtectedMode = false)
    {
        var thread = new LuaState(GlobalState, function, isProtectedMode);
        thread.InheritHook(this);
        return thread;
    }

    void InheritHook(LuaState parent)
    {
        HookMask = parent.HookMask;
        BaseHookCount = parent.BaseHookCount;
        IsLuaDebugHook = parent.IsLuaDebugHook;
        // Lua's debug library registers callbacks per thread. Its mask/count metadata
        // is inherited, but there is no Lua callback registered for the new thread.
        if (IsLuaDebugHook)
        {
            return;
        }

        Hook = parent.Hook;
        IsLineHookEnabled = parent.IsLineHookEnabled;
        CallOrReturnHookMask = parent.CallOrReturnHookMask;
        // lua_newthread copies settings, not the parent's in-progress counter or hook suppression.
        HookCount = BaseHookCount > 0 ? (long)BaseHookCount + 1 : 0;
        LastPc = -1;
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

    public ValueTask<int> ResumeAsync(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (coroutine is not null)
        {
            return coroutine.ResumeAsyncCore(
                context.State.Stack,
                context.ArgumentCount,
                context.ReturnFrameBase,
                context.State,
                cancellationToken
            );
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

    public ValueTask<int> YieldAsync(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (coroutine is not null)
        {
            return coroutine.YieldAsyncCore(
                context.State.Stack,
                context.ArgumentCount,
                context.ReturnFrameBase,
                context.State,
                cancellationToken
            );
        }

        throw new LuaRuntimeException(context.State, "attempt to yield from outside a coroutine");
    }

    public ValueTask<int> YieldAsync(LuaStack stack, CancellationToken cancellationToken = default)
    {
        if (coroutine is not null)
        {
            return coroutine.YieldAsyncCore(stack, stack.Count, 0, null, cancellationToken);
        }

        throw new LuaRuntimeException(null, "attempt to yield from outside a coroutine");
    }

    class ThreadCoreData : IPoolNode<ThreadCoreData>
    {
        // internal LuaCoroutineData? coroutineData;
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
    internal bool IsLuaDebugHook;
    internal byte HookMask;

    public LuaFunction? CoroutineFunction => coroutine?.Function;
    public bool CanResume => GetStatus() == LuaThreadStatus.Suspended;
    public LuaStack Stack => CoreData!.Stack;
    internal Traceback? LuaTraceback => coroutine?.Traceback;
    public LuaTable Environment => GlobalState.Environment;
    public LuaTable Registry => GlobalState.Registry;
    public LuaTable LoadedModules => GlobalState.LoadedModules;
    public LuaTable PreloadModules => GlobalState.PreloadModules;
    public LuaState MainThread => GlobalState.MainThread;

    /// <summary>
    /// Gets or sets the positive call-frame limit shared by this state and its coroutines.
    /// The default is 100,000. Lua and C# frames count toward the limit; tail calls reuse a frame.
    /// Changing this value also applies to existing threads sharing the same global state.
    /// </summary>
    public int MaxCallDepth
    {
        get => GlobalState.MaxCallDepth;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Call depth must be positive."
                );
            }
            GlobalState.MaxCallDepth = value;
        }
    }

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

    internal CallStackFrame CreateCallStackFrame(
        LuaFunction function,
        int argumentCount,
        int returnBase,
        int callerInstructionIndex
    )
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
                LuaVirtualMachine.PrepareVariableArgument(
                    state.Stack,
                    argumentCount,
                    varArgumentCount
                );
            }
        }

        CallStackFrame frame = new()
        {
            Base = state.Stack.Count - argumentCount,
            VariableArgumentCount = varArgumentCount,
            Function = function,
            ReturnBase = returnBase,
            CallerInstructionIndex = callerInstructionIndex,
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
        ref var callStack = ref CoreData!.CallStack;
        if (callStack.Count >= GlobalState.MaxCallDepth)
        {
            throw new LuaStackOverflowException();
        }

        CurrentException?.BuildOrGet();
        CurrentException = null;
        callStack.Push(frame);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ReplaceCallStackFrame(in CallStackFrame frame)
    {
        ref var callStack = ref CoreData!.CallStack;
        if (callStack.Count > GlobalState.MaxCallDepth)
        {
            throw new LuaStackOverflowException();
        }

        // Tail calls retain the depth. Replacing the slot avoids clearing and repushing it,
        // but a pending exception must still capture the outgoing frame before it is overwritten.
        if (CurrentException != null)
        {
            ExceptionTrace.Push(callStack.Peek());
            CurrentException.BuildOrGet();
            CurrentException = null;
        }
        callStack.PeekRef() = frame;
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

    public LuaTable GetCurrentEnvironment()
    {
        var callStack = GetCallStackFrames();
        for (int i = callStack.Length - 1; i >= 0; i--)
        {
            var function = callStack[i].Function;
            if (function is not LuaClosure closure || closure.Proto.UpValues.Length == 0)
            {
                continue;
            }

            if (closure.Proto.UpValues[0].Name != "_ENV")
            {
                continue;
            }

            var upValue = closure.UpValues[0];
            return upValue.GetValue().Read<LuaTable>();
        }

        return Environment;
    }

    /// <summary>Sets the debug hook for this Lua thread, or removes it when <paramref name="hook"/> is null.</summary>
    /// <param name="hook">The callback receiving an event name and a line number (nil for non-line events). Its return values are ignored.</param>
    /// <param name="mask">A combination of 'c' (call), 'r' (return), and 'l' (line).</param>
    /// <param name="count">A positive Lua instruction interval, independent of the mask. Zero disables count events.</param>
    /// <remarks>
    /// The callback can call SetHook to replace or remove itself or change the interval.
    /// Setting the hook resets the count interval; callbacks do not recursively trigger hooks on the same thread.
    /// New threads and coroutines copy host hook settings with an independent instruction counter.
    /// Later changes affect only this thread. Hooks installed by Lua's debug.sethook do not copy their callback.
    /// A shared callback should use its context.State to inspect or reconfigure the executing thread.
    /// Count events track Lua instructions, not C# execution or time spent awaiting host functions.
    /// A host function that calls back into Lua can trigger count events for that Lua execution.
    /// Long-running host functions must cooperate with the execution's CancellationToken to stop promptly.
    /// </remarks>
    public void SetHook(LuaFunction? hook, string mask, int count = 0)
    {
        if (hook is null)
        {
            HookCount = 0;
            BaseHookCount = 0;
            Hook = null;
            HookMask = 0;
            IsLuaDebugHook = false;
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
        HookMask = (byte)(
            (IsCallHookEnabled ? 1 : 0)
            | (IsReturnHookEnabled ? 2 : 0)
            | (IsLineHookEnabled ? 4 : 0)
        );
        IsLuaDebugHook = false;
    }

    internal void SetHookFromLua(LuaFunction? hook, string mask, int count)
    {
        SetHook(hook, mask, count);
        IsLuaDebugHook = hook is not null;
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

    public ValueTask<int> RunAsync(
        LuaFunction function,
        CancellationToken cancellationToken = default
    )
    {
        return RunAsync(function, 0, Stack.Count, cancellationToken);
    }

    public ValueTask<int> RunAsync(
        LuaFunction function,
        int argumentCount,
        CancellationToken cancellationToken = default
    )
    {
        return RunAsync(function, argumentCount, Stack.Count - argumentCount, cancellationToken);
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> RunAsync(
        LuaFunction function,
        int argumentCount,
        int returnBase,
        CancellationToken cancellationToken = default
    )
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
                LuaVirtualMachine.PrepareVariableArgument(
                    state.Stack,
                    argumentCount,
                    varArgumentCount
                );
            }

            argumentCount -= varArgumentCount;
        }

        CallStackFrame frame = new()
        {
            Base = state.Stack.Count - argumentCount,
            VariableArgumentCount = varArgumentCount,
            Function = function,
            ReturnBase = returnBase,
        };
        if (state.IsInHook)
        {
            frame.Flags |= CallStackFrameFlags.InHook;
        }

        state.PushCallStackFrame(frame);
        LuaFunctionExecutionContext context = new()
        {
            State = state,
            ArgumentCount = argumentCount,
            ReturnFrameBase = returnBase,
        };
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

    /// <summary>
    /// Loads .NET source text and a .NET chunk name. In byte-string mode, text is
    /// encoded as UTF-8. Use the byte overload for source that is already encoded.
    /// </summary>
    public LuaClosure Load(ReadOnlySpan<char> chunk, string chunkName, LuaTable? environment = null)
    {
        var name = Platform.EncodeText(chunkName);
        if (Platform.StringMode != LuaStringMode.Bytes)
        {
            return LoadCore(chunk, name, environment);
        }

        var byteCount = Encoding.UTF8.GetByteCount(chunk);
        if (byteCount > 4 && chunk[0] == '\e')
        {
            throw new Exception("attempt to load a binary chunk (mode is 't')");
        }
        // Match the byte-source loader's handling of an initial UTF-8 BOM.
        if (!chunk.IsEmpty && chunk[0] == '\uFEFF')
        {
            chunk = chunk[1..];
            byteCount -= 3;
        }
        if (byteCount == chunk.Length)
        {
            return LoadCore(chunk, name, environment);
        }
        using PooledArray<char> chars = new(byteCount);
        var count = Utf8CharEncoder.Encode(chunk, chars.AsSpan());
        return LoadCore(chars.AsSpan()[..count], name, environment);
    }

    // Source and chunk name already use this state's Lua string representation.
    internal unsafe LuaClosure LoadCore(
        ReadOnlySpan<char> chunk,
        string chunkName,
        LuaTable? environment = null
    )
    {
        Prototype prototype;
        fixed (char* ptr = chunk)
        {
            prototype = Parser.Parse(this, new(ptr, chunk.Length), chunkName);
        }

        return new(this, prototype, environment);
    }

    /// <summary>
    /// Loads encoded source bytes with a .NET chunk name. Byte-string mode preserves
    /// the source bytes, except for an initial UTF-8 BOM.
    /// </summary>
    public LuaClosure Load(
        ReadOnlySpan<byte> chunk,
        string? chunkName = null,
        string mode = "bt",
        LuaTable? environment = null
    )
    {
        return LoadCore(
            chunk,
            chunkName is null ? null : Platform.EncodeText(chunkName),
            mode,
            environment
        );
    }

    // Source is encoded bytes; an explicit chunk name already uses the Lua string representation.
    internal LuaClosure LoadCore(
        ReadOnlySpan<byte> chunk,
        string? chunkName = null,
        string mode = "bt",
        LuaTable? environment = null
    )
    {
        static bool AllowsMode(string mode, char chunkMode)
        {
            return mode.IndexOf(chunkMode) >= 0;
        }

        if (chunk.Length > 4)
        {
            if (chunk[0] == '\e')
            {
                if (!AllowsMode(mode, 'b'))
                {
                    throw new Exception("attempt to load a binary chunk (mode is 't')");
                }

                return new(this, Parser.Undump(chunk, chunkName), environment);
            }
        }

        if (!AllowsMode(mode, 't'))
        {
            throw new Exception("attempt to load a text chunk (mode is 'b')");
        }

        Encoding encoding;
        if (Platform.StringMode == LuaStringMode.Bytes)
        {
            // Byte strings: like luaL_loadfile, skip a UTF-8 BOM and keep every other byte as one char.
            chunk = BomUtility.SkipUtf8Bom(chunk);
            encoding = LuaByteEncoding.Instance;
        }
        else
        {
            chunk = BomUtility.GetEncodingFromBytes(chunk, out encoding);
        }

        var charCount = encoding.GetCharCount(chunk);
        var pooled = ArrayPool<char>.Shared.Rent(charCount);
        try
        {
            var chars = pooled.AsSpan(0, charCount);
            encoding.GetChars(chunk, chars);
            chunkName ??= chars.ToString();

            return LoadCore(chars, chunkName, environment);
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
        if (CoreData == null)
            return;
        if (CoreData.CallStack.Count != 0)
        {
            throw new InvalidOperationException("This state is running! Call stack is not empty!!");
        }

        CoreData.Release();
        CoreData = null!;
    }
}
