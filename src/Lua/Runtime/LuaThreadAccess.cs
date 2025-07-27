using System.Runtime.CompilerServices;

namespace Lua.Runtime;

public readonly struct LuaThreadAccess
{
    internal LuaThreadAccess(LuaState thread, int version)
    {
        State = thread;
        Version = version;
    }

    public readonly LuaState State;
    public readonly int Version;

    public bool IsValid => Version == State.CurrentVersion;

    public LuaGlobalState GlobalState
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ThrowIfInvalid();
            return State.GlobalState;
        }
    }

    public LuaStack Stack
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ThrowIfInvalid();
            return State.Stack;
        }
    }

    public ValueTask<int> RunAsync(LuaFunction function, CancellationToken cancellationToken = default)
    {
        return RunAsync(function, 0, State.Stack.Count, cancellationToken);
    }

    public ValueTask<int> RunAsync(LuaFunction function, int argumentCount, CancellationToken cancellationToken = default)
    {
        return RunAsync(function, argumentCount, State.Stack.Count - argumentCount, cancellationToken);
    }

    public async ValueTask<int> RunAsync(LuaFunction function, int argumentCount, int returnBase, CancellationToken cancellationToken = default)
    {
        ThrowIfInvalid();
        if (function == null)
        {
            throw new ArgumentNullException(nameof(function));
        }

        State.ThrowIfCancellationRequested(cancellationToken);
        var thread = State;
        var varArgumentCount = function.GetVariableArgumentCount(argumentCount);
        if (varArgumentCount != 0)
        {
            if (varArgumentCount < 0)
            {
                thread.Stack.SetTop(thread.Stack.Count - varArgumentCount);
                varArgumentCount = 0;
            }
            else
            {
                LuaVirtualMachine.PrepareVariableArgument(thread.Stack, argumentCount, varArgumentCount);
            }

            argumentCount -= varArgumentCount;
        }

        CallStackFrame frame = new() { Base = thread.Stack.Count - argumentCount, VariableArgumentCount = varArgumentCount, Function = function, ReturnBase = returnBase };
        if (thread.IsInHook)
        {
            frame.Flags |= CallStackFrameFlags.InHook;
        }

        var access = thread.PushCallStackFrame(frame);
        LuaFunctionExecutionContext context = new() { Access = access, ArgumentCount = argumentCount, ReturnFrameBase = returnBase };
        var callStackTop = thread.CallStackFrameCount;
        try
        {
            if (State.CallOrReturnHookMask.Value != 0 && !State.IsInHook)
            {
                return await LuaVirtualMachine.ExecuteCallHook(context, cancellationToken);
            }

            return await function.Func(context, cancellationToken);
        }
        finally
        {
            State.PopCallStackFrameUntil(callStackTop - 1);
        }
    }


    internal CallStackFrame CreateCallStackFrame(LuaFunction function, int argumentCount, int returnBase, int callerInstructionIndex)
    {
        var thread = State;
        var varArgumentCount = function.GetVariableArgumentCount(argumentCount);
        if (varArgumentCount != 0)
        {
            if (varArgumentCount < 0)
            {
                thread.Stack.SetTop(thread.Stack.Count - varArgumentCount);
                argumentCount -= varArgumentCount;
                varArgumentCount = 0;
            }
            else
            {
                LuaVirtualMachine.PrepareVariableArgument(thread.Stack, argumentCount, varArgumentCount);
            }
        }

        CallStackFrame frame = new()
        {
            Base = thread.Stack.Count - argumentCount,
            VariableArgumentCount = varArgumentCount,
            Function = function,
            ReturnBase = returnBase,
            CallerInstructionIndex = callerInstructionIndex
        };

        if (thread.IsInHook)
        {
            frame.Flags |= CallStackFrameFlags.InHook;
        }

        return frame;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ThrowIfInvalid()
    {
        if (Version != State.CurrentVersion)
        {
            ThrowInvalid();
        }
    }

    void ThrowInvalid()
    {
        throw new InvalidOperationException("Thread access is invalid.");
    }
}