using System.Runtime.CompilerServices;

namespace Lua.Runtime;

public readonly struct LuaThreadAccess
{
    internal LuaThreadAccess(LuaThread thread, int version)
    {
        Thread = thread;
        Version = version;
    }

    public readonly LuaThread Thread;
    public readonly int Version;

    public bool IsValid => Version == Thread.CurrentVersion;

    public LuaState State
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ThrowIfInvalid();
            return Thread.State;
        }
    }

    public LuaStack Stack
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ThrowIfInvalid();
            return Thread.Stack;
        }
    }

    public ValueTask<int> RunAsync(LuaFunction function, CancellationToken cancellationToken = default)
    {
        return RunAsync(function, 0, Thread.Stack.Count, cancellationToken);
    }

    public ValueTask<int> RunAsync(LuaFunction function, int argumentCount, CancellationToken cancellationToken = default)
    {
        return RunAsync(function, argumentCount, Thread.Stack.Count - argumentCount, cancellationToken);
    }

    public async ValueTask<int> RunAsync(LuaFunction function, int argumentCount, int returnBase, CancellationToken cancellationToken = default)
    {
        ThrowIfInvalid();
        if (function == null)
        {
            throw new ArgumentNullException(nameof(function));
        }

        Thread.ThrowIfCancellationRequested(cancellationToken);
        var thread = Thread;
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
            if (Thread.CallOrReturnHookMask.Value != 0 && !Thread.IsInHook)
            {
                return await LuaVirtualMachine.ExecuteCallHook(context, cancellationToken);
            }

            return await function.Func(context, cancellationToken);
        }
        finally
        {
            Thread.PopCallStackFrameUntil(callStackTop - 1);
        }
    }


    internal CallStackFrame CreateCallStackFrame(LuaFunction function, int argumentCount, int returnBase, int callerInstructionIndex)
    {
        var thread = Thread;
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
        if (Version != Thread.CurrentVersion)
        {
            ThrowInvalid();
        }
    }

    void ThrowInvalid()
    {
        throw new InvalidOperationException("Thread access is invalid.");
    }
}