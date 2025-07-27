using System.Runtime.CompilerServices;

namespace Lua.Standard.Internal;

static class Bit32Helper
{
    static readonly double Bit32 = 4294967296;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ToUInt32(double d)
    {
        return (uint)ToInt32(d);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToInt32(double d)
    {
        return (int)(long)Math.IEEERemainder(d, Bit32);
    }

    public static void ValidateFieldAndWidth(LuaThread thread, string functionName, int argumentId, int field, int width)
    {
        if (field > 31 || field + width > 32)
            throw new LuaRuntimeException(thread, "trying to access non-existent bits");
        if (field < 0)
            throw new LuaRuntimeException(thread, $"bad argument #{argumentId} to '{functionName}' (field cannot be negative)");

        if (width <= 0)
            throw new LuaRuntimeException(thread, $"bad argument #{argumentId} to '{functionName}' (width must be positive)");
    }
}