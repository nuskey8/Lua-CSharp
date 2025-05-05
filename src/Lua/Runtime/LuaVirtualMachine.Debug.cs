using System.Runtime.CompilerServices;

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
                context.Thread.PopCallStackFrameWithStackPop();
            }

            return false;
        }

        context.Task = r;
        context.Pc--;
        return true;

        static async ValueTask<int> Impl(VirtualMachineExecutionContext context)
        {
            bool countHookIsDone = false;
            var pc = context.Pc;
            var prototype = context.Prototype;
            if (context.Thread.IsCountHookEnabled && --context.Thread.HookCount == 0)
            {
                context.Thread.HookCount = context.Thread.BaseHookCount;

                var hook = context.Thread.Hook!;
                var stack = context.Thread.Stack;
                stack.Push("count");
                stack.Push(LuaValue.Nil);
                var funcContext = new LuaFunctionExecutionContext { Thread = context.Thread, ArgumentCount = 2, ReturnFrameBase = context.Thread.Stack.Count - 2, };
                var frame = new CallStackFrame
                {
                    Base = funcContext.FrameBase,
                    ReturnBase = funcContext.ReturnFrameBase,
                    VariableArgumentCount = hook.GetVariableArgumentCount(funcContext.ArgumentCount),
                    Function = hook,
                    CallerInstructionIndex = context.Pc,
                };
                frame.Flags |= CallStackFrameFlags.InHook;
                context.Thread.IsInHook = true;
                context.Thread.PushCallStackFrame(frame);
                await hook.Func(funcContext, context.CancellationToken);
                context.Thread.IsInHook = false;


                countHookIsDone = true;
            }


            if (context.Thread.IsLineHookEnabled)
            {
                var sourcePositions = prototype.LineInfo;
                var line = sourcePositions[pc];

                if (countHookIsDone || pc == 0 || context.Thread.LastPc < 0 || pc <= context.Thread.LastPc || sourcePositions[context.Thread.LastPc] != line)
                {
                    if (countHookIsDone)
                    {
                        context.Thread.PopCallStackFrameWithStackPop();
                    }


                    var hook = context.Thread.Hook!;
                    var stack = context.Thread.Stack;
                    stack.Push("line");
                    stack.Push(line);
                    var funcContext = new LuaFunctionExecutionContext { Thread = context.Thread, ArgumentCount = 2, ReturnFrameBase = context.Thread.Stack.Count - 2, };
                    var frame = new CallStackFrame
                    {
                        Base = funcContext.FrameBase,
                        ReturnBase = funcContext.ReturnFrameBase,
                        VariableArgumentCount = hook.GetVariableArgumentCount(funcContext.ArgumentCount),
                        Function = hook,
                        CallerInstructionIndex = pc,
                    };
                    frame.Flags |= CallStackFrameFlags.InHook;
                    context.Thread.IsInHook = true;
                    context.Thread.PushCallStackFrame(frame);
                    await hook.Func(funcContext, context.CancellationToken);
                    context.Thread.IsInHook = false;
                    context.Thread.LastPc = pc;
                    return 0;
                }

                context.Thread.LastPc = pc;
            }

            if (countHookIsDone)
            {
                return 0;
            }

            return -1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static ValueTask<int> ExecuteCallHook(VirtualMachineExecutionContext context, in CallStackFrame frame, int arguments, bool isTailCall = false)
    {
        return ExecuteCallHook(new()
        {
            Thread = context.Thread, ArgumentCount = arguments, ReturnFrameBase = frame.ReturnBase
        },context.CancellationToken, isTailCall);
    }

    internal static async ValueTask<int> ExecuteCallHook(LuaFunctionExecutionContext context,  CancellationToken cancellationToken, bool isTailCall = false)
    {
        var argCount = context.ArgumentCount;
        var hook = context.Thread.Hook!;
        var stack = context.Thread.Stack;
        if (context.Thread.IsCallHookEnabled)
        {
            stack.Push((isTailCall ? "tail call" : "call"));

            stack.Push(LuaValue.Nil);
            var funcContext = new LuaFunctionExecutionContext { Thread = context.Thread, ArgumentCount = 2, ReturnFrameBase = context.Thread.Stack.Count - 2, };
            CallStackFrame frame = new()
            {
                Base = funcContext.FrameBase,
                ReturnBase = funcContext.ReturnFrameBase,
                VariableArgumentCount = hook.GetVariableArgumentCount(2),
                Function = hook,
                CallerInstructionIndex = 0,
                Flags = CallStackFrameFlags.InHook
            };

            context.Thread.PushCallStackFrame(frame);
            try
            {
                context.Thread.IsInHook = true;
                await hook.Func(funcContext, cancellationToken);
                context.Thread.PopCallStackFrameWithStackPop();
            }
            finally
            {
                context.Thread.IsInHook = false;
            }
        }

        {
            ref readonly var frame = ref context.Thread.GetCurrentFrame();
            var task = frame.Function.Func(new() { Thread = context.Thread, ArgumentCount = argCount, ReturnFrameBase = frame.ReturnBase, }, cancellationToken);
            var r = await task;
            if (isTailCall || !context.Thread.IsReturnHookEnabled)
            {
                return r;
            }

            stack.Push("return");
            stack.Push(LuaValue.Nil);
            var funcContext = new LuaFunctionExecutionContext { Thread = context.Thread, ArgumentCount = 2, ReturnFrameBase = context.Thread.Stack.Count - 2, };


            context.Thread.PushCallStackFrame(new()
            {
                Base = funcContext.FrameBase,
                ReturnBase = funcContext.ReturnFrameBase,
                VariableArgumentCount = hook.GetVariableArgumentCount(2),
                Function = hook,
                CallerInstructionIndex = 0,
                Flags = CallStackFrameFlags.InHook
            });
            try
            {
                context.Thread.IsInHook = true;
                await hook.Func(funcContext, cancellationToken);
            }
            finally
            {
                context.Thread.IsInHook = false;
            }

            context.Thread.PopCallStackFrameWithStackPop();
            return r;
        }
    }
}