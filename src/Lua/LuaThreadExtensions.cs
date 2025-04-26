using Lua.Internal;

namespace Lua;

public static class LuaThreadExtensions
{
    public static async ValueTask<LuaValue[]> ResumeAsync(this LuaThread thread, LuaState state, CancellationToken cancellationToken = default)
    {
        var frameBase = thread.Stack.Count;
        thread.Stack.Push(thread);

        await thread.ResumeAsync(new()
        {
            State = state,
            Thread = state.CurrentThread,
            ArgumentCount = 1,
            FrameBase = frameBase,
            ReturnFrameBase = frameBase,
        }, cancellationToken);
        var returnBase = ((LuaCoroutine)thread).ReturnFrameBase;
        var results = thread.Stack.AsSpan()[returnBase..].ToArray();
        thread.Stack.PopUntil(returnBase);
        return results;
    }

    public static async ValueTask<LuaValue[]> ResumeAsync(this LuaThread thread, LuaState state, LuaValue[] arguments, CancellationToken cancellationToken = default)
    {
        var frameBase = thread.Stack.Count;
        thread.Stack.Push(thread);
        for (int i = 0; i < arguments.Length; i++)
        {
            thread.Stack.Push(arguments[i]);
        }

        await thread.ResumeAsync(new()
        {
            State = state,
            Thread = state.CurrentThread,
            ArgumentCount = 1 + arguments.Length,
            FrameBase = frameBase,
            ReturnFrameBase = frameBase,
        }, cancellationToken);
        var returnBase = ((LuaCoroutine)thread).ReturnFrameBase;
        var results = thread.Stack.AsSpan()[returnBase..].ToArray();
        thread.Stack.PopUntil(returnBase);
        return results;
    }
}