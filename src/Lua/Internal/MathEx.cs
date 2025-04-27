using System.Runtime.CompilerServices;
#if NET6_0_OR_GREATER
using System.Numerics;
#endif

namespace Lua;

internal static class MathEx
{
    const ulong PositiveInfinityBits = 0x7FF0_0000_0000_0000;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInteger(double d)
    {
#if NET8_0_OR_GREATER
        return double.IsInteger(d);
#else
        return IsFinite(d) && (d == Math.Truncate(d));
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool IsFinite(double d)
    {
#if NET6_0_OR_GREATER
        ulong bits = BitConverter.DoubleToUInt64Bits(d);
#else
        ulong bits = BitCast<double, ulong>(d);
#endif

        return (~bits & PositiveInfinityBits) != 0;
    }

#if !NET6_0_OR_GREATER
    unsafe static TTo BitCast<TFrom, TTo>(TFrom source)
    {
        return Unsafe.ReadUnaligned<TTo>(ref Unsafe.As<TFrom, byte>(ref source));
    }
#endif

    public const int ArrayMexLength = 0x7FFFFFC7;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int NewArrayCapacity(int size)
    {
        var newSize = unchecked(size * 2);
        if ((uint)newSize > ArrayMexLength)
        {
            newSize = ArrayMexLength;
        }

        return newSize;
    }

    const long DBL_EXP_MASK = 0x7ff0000000000000L;
    const int DBL_MANT_BITS = 52;
    const long DBL_SGN_MASK = -1 - 0x7fffffffffffffffL;
    const long DBL_MANT_MASK = 0x000fffffffffffffL;
    const long DBL_EXP_CLR_MASK = DBL_SGN_MASK | DBL_MANT_MASK;

    public static (double m, int e) Frexp(double d)
    {
        var bits = BitConverter.DoubleToInt64Bits(d);
        var exp = (int)((bits & DBL_EXP_MASK) >> DBL_MANT_BITS);
        var e = 0;

        if (exp == 0x7ff || d == 0D)
            d += d;
        else
        {
            // Not zero and finite.
            e = exp - 1022;
            if (exp == 0)
            {
                // Subnormal, scale d so that it is in [1, 2).
                d *= BitConverter.Int64BitsToDouble(0x4350000000000000L); // 2^54
                bits = BitConverter.DoubleToInt64Bits(d);
                exp = (int)((bits & DBL_EXP_MASK) >> DBL_MANT_BITS);
                e = exp - 1022 - 54;
            }

            // Set exponent to -1 so that d is in [0.5, 1).
            d = BitConverter.Int64BitsToDouble((bits & DBL_EXP_CLR_MASK) | 0x3fe0000000000000L);
        }

        return (d, e);
    }

    public static (int i, double f) Modf(double d)
    {
        return ((int)Math.Truncate(d), d % 1.0);
    }

    /// <summary>Returns the smallest power of two greater than or equal to the input.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int NextPowerOfTwo(int x)
    {
#if NET6_0_OR_GREATER
        if (x <= 0) return 0;
        return (int)BitOperations.RoundUpToPowerOf2((uint)x);
#else
        if (x <= 0) return 0;
        x -= 1;
        x |= x >> 1;
        x |= x >> 2;
        x |= x >> 4;
        x |= x >> 8;
        x |= x >> 16;
        return x + 1;
#endif
    }
}