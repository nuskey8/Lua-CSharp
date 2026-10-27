using Lua;
using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

public sealed class LuaCSharpComparison : IDisposable
{
    readonly LuaState state;
    readonly LuaClosure closure;
    readonly Lua.LuaValue[] results = new Lua.LuaValue[1];

    public LuaCSharpComparison(string source, bool bytes = false)
    {
        state = LuaState.Create(bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default);
        state.OpenStandardLibraries();
        state.Environment["host_add"] = new Lua.LuaFunction(
            (context, token) =>
                new(context.Return(context.GetArgument<double>(0) + context.GetArgument<double>(1)))
        );
        closure = state.Load(source, "comparison");
    }

    public double Run()
    {
        // These workloads have no asynchronous host functions. Preserve the allocation-free
        // synchronous ValueTask path instead of introducing a Task wrapper in the benchmark.
        var pending = state.ExecuteAsync(closure, results);
        if (pending.IsCompleted)
            pending.GetAwaiter().GetResult();
        else
            pending.AsTask().GetAwaiter().GetResult();
        return results[0].Read<double>();
    }

    public void Dispose() => state.Dispose();
}
