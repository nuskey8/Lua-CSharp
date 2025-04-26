using Lua.Runtime;

namespace Lua;

public class LuaFunction(string name, Func<LuaFunctionExecutionContext, CancellationToken, ValueTask<int>> func)
{
    public string Name { get; } = name;
    internal Func<LuaFunctionExecutionContext, CancellationToken, ValueTask<int>> Func { get; } = func;

    public LuaFunction(Func<LuaFunctionExecutionContext, CancellationToken, ValueTask<int>> func) : this("anonymous", func)
    {
    }

    public async ValueTask<int> InvokeAsync(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var frame = new CallStackFrame { Base = context.FrameBase, VariableArgumentCount = this.GetVariableArgumentCount(context.ArgumentCount), Function = this, ReturnBase = context.ReturnFrameBase };
        context.Thread.PushCallStackFrame(frame);
        try
        {
            if (context.Thread.CallOrReturnHookMask.Value != 0 && !context.Thread.IsInHook)
            {
                return await LuaVirtualMachine.ExecuteCallHook(context, cancellationToken);
            }

            return await Func(context, cancellationToken);
        }
        finally
        {
            context.Thread.PopCallStackFrame();
        }
    }
}