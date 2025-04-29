using Lua.Runtime;

namespace Lua;

public static class LuaFunctionExtensions
{
    public static async ValueTask<int> InvokeAsync(this LuaFunction function, LuaThread thread, int argumentCount, CancellationToken cancellationToken = default)
    {
        var varArgumentCount = function.GetVariableArgumentCount(argumentCount);
        if (varArgumentCount != 0)
        {
            LuaVirtualMachine.PrepareVariableArgument(thread.Stack, argumentCount, varArgumentCount);
        }

        LuaFunctionExecutionContext context = new() { Thread = thread, ArgumentCount = argumentCount - varArgumentCount, ReturnFrameBase = thread.Stack.Count, };
        var frame = new CallStackFrame { Base = context.FrameBase, VariableArgumentCount = varArgumentCount, Function = function, ReturnBase = context.ReturnFrameBase };
        context.Thread.PushCallStackFrame(frame);
        try
        {
            if (context.Thread.CallOrReturnHookMask.Value != 0 && !context.Thread.IsInHook)
            {
                return await LuaVirtualMachine.ExecuteCallHook(context, cancellationToken);
            }

            return await function.Func(context, cancellationToken);
        }
        finally
        {
            context.Thread.PopCallStackFrame();
        }
    }
}