using Lua.Runtime;

namespace Lua.Standard;

public sealed class CoroutineLibrary
{
    public static readonly CoroutineLibrary Instance = new();

    public CoroutineLibrary()
    {
        var libraryName = "coroutine";
        Functions =
        [
            new(libraryName, "create", Create),
            new(libraryName, "resume", Resume),
            new(libraryName, "running", Running),
            new(libraryName, "status", Status),
            new(libraryName, "wrap", Wrap),
            new(libraryName, "yield", Yield)
        ];
    }

    public readonly LibraryFunction[] Functions;

    public ValueTask<int> Create(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaFunction>(0);
        return new(context.Return(LuaState.CreateCoroutine(context.State.GlobalState, arg0, true)));
    }

    public ValueTask<int> Resume(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var state = context.GetArgument<LuaState>(0);
        return state.ResumeAsync(context with { ArgumentCount = context.ArgumentCount - 1 }, cancellationToken);
    }

    public ValueTask<int> Running(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        return new(context.Return(context.State, context.State == context.GlobalState.MainThread));
    }

    public ValueTask<int> Status(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var state = context.GetArgument<LuaState>(0);
        return new(context.Return(state.GetStatus() switch
        {
            LuaThreadStatus.Normal => "normal",
            LuaThreadStatus.Suspended => "suspended",
            LuaThreadStatus.Running => "running",
            LuaThreadStatus.Dead => "dead",
            _ => ""
        }));
    }

    public ValueTask<int> Wrap(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaFunction>(0);
        var state = LuaState.CreateCoroutine(context.State.GlobalState, arg0, false);
        return new(context.Return(new CSharpClosure("wrap", [state],
            static async (context, cancellationToken) =>
            {
                var state = context.GetCsClosure()!.UpValues[0].Read<LuaState>();
                if (!state.IsCoroutine)
                {
                    return await state.ResumeAsync(context, cancellationToken);
                }

                var stack = context.State.Stack;
                var frameBase = stack.Count;
                context.State.PushCallStackFrame(new() { Base = frameBase, ReturnBase = context.ReturnFrameBase, VariableArgumentCount = 0, Function = state.CoroutineFunction! });
                try
                {
                    await state.ResumeAsync(context, cancellationToken);
                    var result = context.GetReturnBuffer(context.State.Stack.Count - context.ReturnFrameBase);
                    result[1..].CopyTo(result);
                    context.State.Stack.Pop();
                    return result.Length - 1;
                }
                finally
                {
                    context.State.PopCallStackFrame();
                }
            })));
    }

    public ValueTask<int> Yield(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        return context.State.YieldAsync(context, cancellationToken);
    }
}