using System.Runtime.CompilerServices;

namespace Lua.Runtime;

static class LuaRuntimeExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetMetamethod(
        this LuaValue value,
        LuaGlobalState globalState,
        string methodName,
        out LuaValue result
    )
    {
        result = default;
        return globalState.TryGetMetatable(value, out var metatable)
            && metatable.TryGetValue(methodName, out result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetArgumentAdjustment(this LuaFunction function, int argumentCount)
    {
        if (function is not LuaClosure closure)
            return 0;
        var adjustment = argumentCount - closure.Proto.ParameterCount;
        return adjustment <= 0 || closure.Proto.HasVariableArguments ? adjustment : 0;
    }
}
