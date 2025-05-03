using Lua.Runtime;

namespace Lua.Standard;

public sealed class CoroutineLibrary
{
    public static readonly CoroutineLibrary Instance = new();

    public CoroutineLibrary()
    {
        Functions =
        [
            new("create", Create),
            new("resume", Resume),
            new("running", Running),
            new("status", Status),
            new("wrap", Wrap),
            new("yield", Yield),
        ];
    }

    public readonly LuaFunction[] Functions;

    public ValueTask<int> Create(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaFunction>(0);
        return new(context.Return(LuaCoroutine.Create(context.Thread, arg0, true)));
    }

    public ValueTask<int> Resume(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var thread = context.GetArgument<LuaThread>(0);
        return thread.ResumeAsync(context with { ArgumentCount = context.ArgumentCount - 1 }, cancellationToken);
    }

    public ValueTask<int> Running(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        return new(context.Return(context.Thread, context.Thread == context.State.MainThread));
    }

    public ValueTask<int> Status(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var thread = context.GetArgument<LuaThread>(0);
        return new(context.Return(thread.GetStatus() switch
        {
            LuaThreadStatus.Normal => "normal",
            LuaThreadStatus.Suspended => "suspended",
            LuaThreadStatus.Running => "running",
            LuaThreadStatus.Dead => "dead",
            _ => "",
        }));
    }

    public ValueTask<int> Wrap(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaFunction>(0);
        var thread = LuaCoroutine.Create(context.Thread, arg0, false);
        return new(context.Return(new CSharpClosure("wrap", [thread],
            static async (context, cancellationToken) =>
            {
                var thread = context.GetCsClosure()!.UpValues[0].Read<LuaThread>();
                if (thread is not LuaCoroutine coroutine)
                {
                    return await thread.ResumeAsync(context, cancellationToken);
                }

                var stack = context.Thread.Stack;
                var frameBase = stack.Count;
                context.Thread.PushCallStackFrame(new() { Base = frameBase, ReturnBase = context.ReturnFrameBase, VariableArgumentCount = 0, Function = coroutine.Function });
                try
                {
                    await thread.ResumeAsync(context, cancellationToken);
                    var result = context.GetReturnBuffer(context.Thread.Stack.Count - context.ReturnFrameBase);
                    result[1..].CopyTo(result);
                    context.Thread.Stack.Pop();
                    return result.Length - 1;
                }
                finally
                {
                    context.Thread.PopCallStackFrame();
                }
            })));
    }

    public ValueTask<int> Yield(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        return context.Thread.YieldAsync(context, cancellationToken);
    }
}