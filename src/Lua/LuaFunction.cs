using System.Runtime.CompilerServices;

namespace Lua;

public class LuaFunction(
    string name,
    Func<LuaFunctionExecutionContext, CancellationToken, ValueTask<int>> func
)
{
    public string Name { get; } = name;

    // All delegate calls grow the C# stack, including RunAsync, hooks and metamethods.
    // Pure Lua-to-Lua calls stay in the VM loop and do not invoke this method.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ValueTask<int> Func(LuaFunctionExecutionContext context, CancellationToken ct)
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            throw new LuaStackOverflowException();
        }
        return func(context, ct);
    }

    public LuaFunction(Func<LuaFunctionExecutionContext, CancellationToken, ValueTask<int>> func)
        : this("anonymous", func) { }
}
