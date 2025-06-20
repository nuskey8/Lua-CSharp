namespace Lua.Standard;

public readonly record struct LibraryFunction(string Name, LuaFunction Func)
{
    public LibraryFunction(string libraryName, string name, Func<LuaFunctionExecutionContext, CancellationToken, ValueTask<int>> function) : this(name, new LuaFunction(libraryName + "." + name, function))
    {
    }
}