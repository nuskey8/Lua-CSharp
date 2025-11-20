namespace Lua;

public class LuaFunction(string name, Func<LuaFunctionExecutionContext, CancellationToken, ValueTask<int>> func)
{
    public string Name { get; } = name;
    internal Func<LuaFunctionExecutionContext, CancellationToken, ValueTask<int>> Func { get; } = func;

    public LuaFunction(Func<LuaFunctionExecutionContext, CancellationToken, ValueTask<int>> func) : this("anonymous", func)
    {
    }
}