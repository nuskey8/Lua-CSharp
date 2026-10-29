using System.Runtime.CompilerServices;
using Lua.Internal.CompilerServices;

namespace Lua.Runtime;

public static partial class LuaVirtualMachine
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool ExecutePerInstructionHook(VirtualMachineExecutionContext context)
    {
        var r = Impl(context);
        if (r.IsCompleted)
        {
            if (r.Result == 0)
            {
                context.State.PopCallStackFrameWithStackPop();
            }

            return false;
        }

        context.Task = r;
        context.Pc--;
        return true;

        [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
        static async ValueTask<int> Impl(VirtualMachineExecutionContext context)
        {
            var countHookIsDone = false;
            var pc = context.Pc;
            var prototype = context.Prototype;
            if (context.State.HookCount == 0)
            {
                context.State.HookCount = context.State.BaseHookCount + 1;

                var hook = context.State.Hook!;

                var stack = context.State.Stack;
                var top = stack.Count;
                stack.PushRange("count", LuaValue.Nil);
                context.State.IsInHook = true;
                try
                {
                    var frame = context.State.CreateCallStackFrame(hook, 2, top, pc);
                    context.State.PushCallStackFrame(frame);
                    LuaFunctionExecutionContext funcContext = new()
                    {
                        State = context.State,
                        ArgumentCount = stack.Count - frame.Base,
                        ReturnFrameBase = frame.ReturnBase,
                    };
                    await hook.Func(funcContext, context.CancellationToken);
                }
                finally
                {
                    // An aborted hook must not suppress hooks when this state is reused.
                    // Frame creation can also fail before the callback is invoked.
                    context.State.IsInHook = false;
                }

                countHookIsDone = true;
            }

            context.ThrowIfCancellationRequested();
            if (context.State.IsLineHookEnabled)
            {
                var sourcePositions = prototype.LineInfo;
                var line = sourcePositions.IsEmpty ? 0 : sourcePositions[pc];

                if (
                    countHookIsDone
                    || pc == 0
                    || context.State.LastPc < 0
                    || pc <= context.State.LastPc
                    || (!sourcePositions.IsEmpty && sourcePositions[context.State.LastPc] != line)
                )
                {
                    if (countHookIsDone)
                    {
                        context.State.PopCallStackFrameWithStackPop();
                    }

                    var hook = context.State.Hook!;
                    var stack = context.State.Stack;
                    var top = stack.Count;
                    stack.PushRange("line", line);
                    context.State.IsInHook = true;
                    try
                    {
                        var frame = context.State.CreateCallStackFrame(hook, 2, top, pc);
                        context.State.PushCallStackFrame(frame);
                        LuaFunctionExecutionContext funcContext = new()
                        {
                            State = context.State,
                            ArgumentCount = stack.Count - frame.Base,
                            ReturnFrameBase = frame.ReturnBase,
                        };
                        await hook.Func(funcContext, context.CancellationToken);
                    }
                    finally
                    {
                        context.State.IsInHook = false;
                    }

                    context.State.LastPc = pc;
                    return 0;
                }

                context.State.LastPc = pc;
            }

            if (countHookIsDone)
            {
                return 0;
            }

            return -1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static ValueTask<int> ExecuteCallHook(
        VirtualMachineExecutionContext context,
        in CallStackFrame frame,
        int arguments,
        bool isTailCall = false
    )
    {
        return ExecuteCallHook(
            new()
            {
                State = context.State,
                ArgumentCount = arguments,
                ReturnFrameBase = frame.ReturnBase,
            },
            context.CancellationToken,
            isTailCall
        );
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    internal static async ValueTask<int> ExecuteCallHook(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken,
        bool isTailCall = false
    )
    {
        var argCount = context.ArgumentCount;
        var stack = context.State.Stack;
        {
            ref readonly var frame = ref context.State.GetCurrentFrame();
            if (frame.Function is LuaClosure closure)
                stack.PopUntil(frame.Base + closure.Proto.MaxStackSize);
        }
        if (context.State.IsCallHookEnabled)
        {
            await ExecuteCallOrReturnHook(
                context.State,
                isTailCall ? "tail call" : "call",
                0,
                cancellationToken
            );
        }

        context.State.ThrowIfCancellationRequested(cancellationToken);

        {
            var task = context
                .State.GetCurrentFrame()
                .Function.Func(
                    new()
                    {
                        State = context.State,
                        ArgumentCount = argCount,
                        ReturnFrameBase = context.ReturnFrameBase,
                    },
                    cancellationToken
                );
            var r = await task;
            if (
                isTailCall
                || context.State.GetCurrentFrame().Function is LuaClosure
                || !context.State.IsReturnHookEnabled
            )
            {
                return r;
            }

            context.State.ThrowIfCancellationRequested(cancellationToken);
            await ExecuteCallOrReturnHook(context.State, "return", 0, cancellationToken);
            return r;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool ExecuteReturnHook(VirtualMachineExecutionContext context)
    {
        // Return hooks inspect the callee before its registers are reclaimed.
        context.PostOperation = PostOperationType.Return;
        var task = ExecuteCallOrReturnHook(
            context.State,
            "return",
            context.Pc,
            context.CancellationToken
        );
        if (!task.IsCompleted)
        {
            context.Task = task;
            return true;
        }
        task.GetAwaiter().GetResult();
        context.PostOperation = PostOperationType.None;
        return false;
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    static async ValueTask<int> ExecuteCallOrReturnHook(
        LuaState state,
        string eventName,
        int pc,
        CancellationToken cancellationToken
    )
    {
        var hook = state.Hook!;
        var stack = state.Stack;
        var top = stack.Count;
        stack.PushRange(eventName, LuaValue.Nil);
        state.IsInHook = true;
        var framePushed = false;
        try
        {
            var frame = state.CreateCallStackFrame(hook, 2, top, pc);
            state.PushCallStackFrame(frame);
            framePushed = true;
            LuaFunctionExecutionContext context = new()
            {
                State = state,
                ArgumentCount = stack.Count - frame.Base,
                ReturnFrameBase = frame.ReturnBase,
            };
            await hook.Func(context, cancellationToken);
        }
        finally
        {
            state.IsInHook = false;
            if (framePushed)
                state.PopCallStackFrameWithStackPop();
        }
        return 0;
    }
}
