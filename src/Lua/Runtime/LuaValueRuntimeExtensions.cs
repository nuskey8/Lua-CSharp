using System.Runtime.CompilerServices;

namespace Lua.Runtime;

static class LuaRuntimeExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetMetamethod(this LuaValue value, LuaGlobalState globalState, string methodName, out LuaValue result)
    {
        result = default;
        return globalState.TryGetMetatable(value, out var metatable) &&
               metatable.TryGetValue(methodName, out result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetVariableArgumentCount(this LuaFunction function, int argumentCount)
    {
        return function is LuaClosure { Proto.HasVariableArguments: true } luaClosure
            ? argumentCount - luaClosure.Proto.ParameterCount
            : 0;
    }
}