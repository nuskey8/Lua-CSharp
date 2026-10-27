using System.Runtime.CompilerServices;

namespace Lua.Standard.Internal;

static class Bit32Helper
{
    const double Bit32 = 4294967296;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ToUInt32(double d)
    {
        return (uint)ToInt32(d);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToInt32(double d)
    {
        // Lua 5.2 allows fractional words; use round-to-even like its IEEE754 conversion.
        return (int)(long)(Math.Round(d) % Bit32);
    }

    public static void ValidateFieldAndWidth(
        LuaState state,
        string functionName,
        int argumentId,
        int field,
        int width
    )
    {
        if (field < 0)
            throw new LuaRuntimeException(
                state,
                $"bad argument #{argumentId} to '{functionName}' (field cannot be negative)"
            );

        if (width <= 0)
            throw new LuaRuntimeException(
                state,
                $"bad argument #{argumentId + 1} to '{functionName}' (width must be positive)"
            );

        if (field > 31 || width > 32 - field)
            throw new LuaRuntimeException(state, "trying to access non-existent bits");
    }
}
