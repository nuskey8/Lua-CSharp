using System.Runtime.CompilerServices;

namespace Lua;

/// <summary>A Lua function implemented by a managed callback.</summary>
/// <param name="name">The function name used in diagnostics.</param>
/// <param name="func">
/// Writes results starting at <see cref="LuaFunctionExecutionContext.ReturnFrameBase"/>,
/// sets the stack top just past those results, and returns their count.
/// Return the value of <see cref="LuaFunctionExecutionContext.Return()"/>, another
/// Return overload, or the count passed to GetReturnBuffer. A zero count means
/// no results, not one nil result. The same contract applies after suspension.
/// </param>
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
