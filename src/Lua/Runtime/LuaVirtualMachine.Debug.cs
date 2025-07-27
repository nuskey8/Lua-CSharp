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
                context.State.PopCallStackFrameWithStackPop();
            }

            return false;
        }

        context.Task = r;
        context.Pc--;
        return true;

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
                stack.Push("count");
                stack.Push(LuaValue.Nil);
                context.State.IsInHook = true;
                var frame = context.State.CurrentAccess.CreateCallStackFrame(hook, 2, top, pc);
                var access = context.State.PushCallStackFrame(frame);
                LuaFunctionExecutionContext funcContext = new() { Access = access, ArgumentCount = stack.Count - frame.Base, ReturnFrameBase = frame.ReturnBase };
                await hook.Func(funcContext, context.CancellationToken);
                context.State.IsInHook = false;

                countHookIsDone = true;
            }

            context.ThrowIfCancellationRequested();
            if (context.State.IsLineHookEnabled)
            {
                var sourcePositions = prototype.LineInfo;
                var line = sourcePositions[pc];

                if (countHookIsDone || pc == 0 || context.State.LastPc < 0 || pc <= context.State.LastPc || sourcePositions[context.State.LastPc] != line)
                {
                    if (countHookIsDone)
                    {
                        context.State.PopCallStackFrameWithStackPop();
                    }


                    var hook = context.State.Hook!;
                    var stack = context.State.Stack;
                    var top = stack.Count;
                    stack.Push("line");
                    stack.Push(line);
                    context.State.IsInHook = true;
                    var frame = context.State.CurrentAccess.CreateCallStackFrame(hook, 2, top, pc);
                    var access = context.State.PushCallStackFrame(frame);
                    LuaFunctionExecutionContext funcContext = new() { Access = access, ArgumentCount = stack.Count - frame.Base, ReturnFrameBase = frame.ReturnBase };
                    try
                    {
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
    static ValueTask<int> ExecuteCallHook(VirtualMachineExecutionContext context, in CallStackFrame frame, int arguments, bool isTailCall = false)
    {
        return ExecuteCallHook(new() { Access = context.State.CurrentAccess, ArgumentCount = arguments, ReturnFrameBase = frame.ReturnBase }, context.CancellationToken, isTailCall);
    }

    internal static async ValueTask<int> ExecuteCallHook(LuaFunctionExecutionContext context, CancellationToken cancellationToken, bool isTailCall = false)
    {
        var argCount = context.ArgumentCount;
        var hook = context.State.Hook!;
        var stack = context.State.Stack;
        if (context.State.IsCallHookEnabled)
        {
            var top = stack.Count;
            stack.Push(isTailCall ? "tail call" : "call");

            stack.Push(LuaValue.Nil);
            context.State.IsInHook = true;
            var frame = context.State.CurrentAccess.CreateCallStackFrame(hook, 2, top, 0);
            var access = context.State.PushCallStackFrame(frame);
            LuaFunctionExecutionContext funcContext = new() { Access = access, ArgumentCount = stack.Count - frame.Base, ReturnFrameBase = frame.ReturnBase };
            try
            {
                await hook.Func(funcContext, cancellationToken);
            }
            finally
            {
                context.State.IsInHook = false;
                context.State.PopCallStackFrameWithStackPop();
            }
        }

        context.State.ThrowIfCancellationRequested(cancellationToken);

        {
            var frame = context.State.GetCurrentFrame();
            var task = frame.Function.Func(new() { Access = context.State.CurrentAccess, ArgumentCount = argCount, ReturnFrameBase = frame.ReturnBase }, cancellationToken);
            var r = await task;
            if (isTailCall || !context.State.IsReturnHookEnabled)
            {
                return r;
            }

            context.State.ThrowIfCancellationRequested(cancellationToken);
            var top = stack.Count;
            stack.Push("return");
            stack.Push(LuaValue.Nil);
            context.State.IsInHook = true;
            frame = context.State.CurrentAccess.CreateCallStackFrame(hook, 2, top, 0);
            var access = context.State.PushCallStackFrame(frame);
            LuaFunctionExecutionContext funcContext = new() { Access = access, ArgumentCount = stack.Count - frame.Base, ReturnFrameBase = frame.ReturnBase };
            try
            {
                context.State.IsInHook = true;
                await hook.Func(funcContext, cancellationToken);
            }
            finally
            {
                context.State.IsInHook = false;
                context.State.PopCallStackFrameWithStackPop();
            }

            return r;
        }
    }
}