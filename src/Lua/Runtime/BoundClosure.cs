namespace Lua.Runtime;

/// <summary>A managed Lua function with a bound target.</summary>
/// <typeparam name="T">The type of the bound target.</typeparam>
/// <param name="name">The function name used in diagnostics.</param>
/// <param name="target">The target retained by this closure.</param>
/// <param name="func">The managed callback, following the result contract of <see cref="LuaFunction"/>.</param>
/// <remarks>
/// Retrieve this closure through <c>context.State.GetCurrentFrame().Function</c>
/// inside the callback to access its target. The target is separate from Lua
/// arguments and upvalues; this type does not insert or remove a self argument.
/// </remarks>
public sealed class BoundClosure<T>(
    string name,
    T target,
    Func<LuaFunctionExecutionContext, CancellationToken, ValueTask<int>> func
) : LuaFunction(name, func)
{
    /// <summary>Gets the target retained by this closure.</summary>
    public T Target { get; } = target;
}
