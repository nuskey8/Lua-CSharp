using System.Runtime.CompilerServices;
using Lua.Runtime;

namespace Lua;

public class LuaFunction(string name, Func<LuaFunctionExecutionContext, Memory<LuaValue>, CancellationToken, ValueTask<int>> func)
{
    public string Name { get; set; } = name;
    internal Func<LuaFunctionExecutionContext, Memory<LuaValue>, CancellationToken, ValueTask<int>> Func { get; } = func;

    public LuaFunction(Func<LuaFunctionExecutionContext, Memory<LuaValue>, CancellationToken, ValueTask<int>> func) : this("anonymous", func)
    {
    }

    public async ValueTask<int> InvokeAsync(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var frame = new CallStackFrame
        {
            Base = context.FrameBase,
            VariableArgumentCount = this is Closure closure ? Math.Max(context.ArgumentCount - closure.Proto.ParameterCount, 0) : 0,
            Function = this,
        };

        context.Thread.PushCallStackFrame(frame);
        try
        {
            return await Func(context, buffer, cancellationToken);
        }
        finally
        {
            context.Thread.PopCallStackFrame();
        }
    }
}
