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
            var countHookIsDone = false;
            var pc = context.Pc;
            var prototype = context.Prototype;
            if (context.Thread.HookCount == 0)
            {
                context.Thread.HookCount = context.Thread.BaseHookCount + 1;

                var hook = context.Thread.Hook!;

                var stack = context.Thread.Stack;
                var top = stack.Count;
                stack.Push("count");
                stack.Push(LuaValue.Nil);
                context.Thread.IsInHook = true;
                var frame = context.Thread.CurrentAccess.CreateCallStackFrame(hook, 2, top, pc);
                var access = context.Thread.PushCallStackFrame(frame);
                LuaFunctionExecutionContext funcContext = new() { Access = access, ArgumentCount = stack.Count - frame.Base, ReturnFrameBase = frame.ReturnBase };
                await hook.Func(funcContext, context.CancellationToken);
                context.Thread.IsInHook = false;

                countHookIsDone = true;
            }

            context.ThrowIfCancellationRequested();
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
                    var top = stack.Count;
                    stack.Push("line");
                    stack.Push(line);
                    context.Thread.IsInHook = true;
                    var frame = context.Thread.CurrentAccess.CreateCallStackFrame(hook, 2, top, pc);
                    var access = context.Thread.PushCallStackFrame(frame);
                    LuaFunctionExecutionContext funcContext = new() { Access = access, ArgumentCount = stack.Count - frame.Base, ReturnFrameBase = frame.ReturnBase };
                    try
                    {
                        await hook.Func(funcContext, context.CancellationToken);
                    }
                    finally
                    {
                        context.Thread.IsInHook = false;
                    }

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
        return ExecuteCallHook(new() { Access = context.Thread.CurrentAccess, ArgumentCount = arguments, ReturnFrameBase = frame.ReturnBase }, context.CancellationToken, isTailCall);
    }

    internal static async ValueTask<int> ExecuteCallHook(LuaFunctionExecutionContext context, CancellationToken cancellationToken, bool isTailCall = false)
    {
        var argCount = context.ArgumentCount;
        var hook = context.Thread.Hook!;
        var stack = context.Thread.Stack;
        if (context.Thread.IsCallHookEnabled)
        {
            var top = stack.Count;
            stack.Push(isTailCall ? "tail call" : "call");

            stack.Push(LuaValue.Nil);
            context.Thread.IsInHook = true;
            var frame = context.Thread.CurrentAccess.CreateCallStackFrame(hook, 2, top, 0);
            var access = context.Thread.PushCallStackFrame(frame);
            LuaFunctionExecutionContext funcContext = new() { Access = access, ArgumentCount = stack.Count - frame.Base, ReturnFrameBase = frame.ReturnBase };
            try
            {
                await hook.Func(funcContext, cancellationToken);
            }
            finally
            {
                context.Thread.IsInHook = false;
                context.Thread.PopCallStackFrameWithStackPop();
            }
        }

        context.Thread.ThrowIfCancellationRequested(cancellationToken);

        {
            var frame = context.Thread.GetCurrentFrame();
            var task = frame.Function.Func(new() { Access = context.Thread.CurrentAccess, ArgumentCount = argCount, ReturnFrameBase = frame.ReturnBase }, cancellationToken);
            var r = await task;
            if (isTailCall || !context.Thread.IsReturnHookEnabled)
            {
                return r;
            }

            context.Thread.ThrowIfCancellationRequested(cancellationToken);
            var top = stack.Count;
            stack.Push("return");
            stack.Push(LuaValue.Nil);
            context.Thread.IsInHook = true;
            frame = context.Thread.CurrentAccess.CreateCallStackFrame(hook, 2, top, 0);
            var access = context.Thread.PushCallStackFrame(frame);
            LuaFunctionExecutionContext funcContext = new() { Access = access, ArgumentCount = stack.Count - frame.Base, ReturnFrameBase = frame.ReturnBase };
            try
            {
                context.Thread.IsInHook = true;
                await hook.Func(funcContext, cancellationToken);
            }
            finally
            {
                context.Thread.IsInHook = false;
                context.Thread.PopCallStackFrameWithStackPop();
            }

            return r;
        }
    }
}