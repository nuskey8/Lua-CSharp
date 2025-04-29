using Lua.Internal;

namespace Lua;

public static class LuaFunctionExtensions
{
    // public static async ValueTask<LuaValue[]> InvokeAsync(this LuaFunction function, LuaState state, LuaValue[] arguments, CancellationToken cancellationToken = default)
    // {
    //     var thread = state.CurrentThread;
    //     var frameBase = thread.Stack.Count;
    //
    //     for (int i = 0; i < arguments.Length; i++)
    //     {
    //         thread.Stack.Push(arguments[i]);
    //     }
    //
    //     var resultCount = await function.InvokeAsync(new()
    //     {
    //         State = state,
    //         Thread = thread,
    //         ArgumentCount = arguments.Length,
    //         ReturnFrameBase = frameBase,
    //     }, cancellationToken);
    //     var r = thread.Stack.GetBuffer()[frameBase..(frameBase + resultCount)].ToArray();
    //     thread.Stack.PopUntil(frameBase);
    //     return r;
    // }
}