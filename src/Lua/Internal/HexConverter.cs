using System.Globalization;
using System.Numerics;

namespace Lua.Internal;

public static class HexConverter
{
    public static double ToDouble(ReadOnlySpan<char> text)
    {
        var sign = 1;
        text = text.Trim();
        var first = text[0];
        if (first == '+')
        {
            // Remove the "+0x"
            sign = 1;
            text = text[3..];
        }
        else if (first == '-')
        {
            // Remove the "-0x"
            sign = -1;
            text = text[3..];
        }
        else
        {
            // Remove the "0x"
            text = text[2..];
        }

        var dotIndex = text.IndexOf('.');
        var expIndex = text.IndexOfAny('p', 'P');

        if (dotIndex == -1 && expIndex == -1)
        {
            // unsigned big integer
            // TODO: optimize
            using PooledArray<char> buffer = new(text.Length + 1);
            text.CopyTo(buffer.AsSpan()[1..]);
            buffer[0] = '0';
            return sign * (double)BigInteger.Parse(buffer.AsSpan()[..(text.Length + 1)], NumberStyles.AllowHexSpecifier);
        }

        ReadOnlySpan<char> intPart;
        ReadOnlySpan<char> decimalPart;
        ReadOnlySpan<char> expPart;

        if (dotIndex == -1)
        {
            intPart = text[..expIndex];
            decimalPart = [];
            expPart = text[(expIndex + 1)..];
        }
        else if (expIndex == -1)
        {
            intPart = text[..dotIndex];
            decimalPart = text[(dotIndex + 1)..];
            expPart = [];
        }
        else
        {
            intPart = text[..dotIndex];
            decimalPart = text.Slice(dotIndex + 1, expIndex - dotIndex - 1);
            expPart = text[(expIndex + 1)..];
        }

        var value = intPart.Length == 0
            ? 0
            : long.Parse(intPart, NumberStyles.AllowHexSpecifier);

        var decimalValue = 0.0;
        for (var i = 0; i < decimalPart.Length; i++)
        {
            decimalValue += ToInt(decimalPart[i]) * Math.Pow(16, -(i + 1));
        }

        var result = value + decimalValue;

        if (expPart.Length > 0)
        {
            result *= Math.Pow(2, int.Parse(expPart));
        }

        return result * sign;
    }

    static int ToInt(char c)
    {
        unchecked
        {
            switch (c)
            {
                case < '0':
                    return 0;
                case <= '9':
                    return c - '0';
                case >= 'A' and <= 'F':
                    return c - 'A' + 10;
                case >= 'a' and <= 'f':
                    return c - 'a' + 10;
            }
        }

        return 0;
    }

    public static string FromDouble(double value)
    {
        if (double.IsNaN(value))
        {
            return "(0/0)";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "1e9999";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "-1e9999";
        }

        if (value == 0.0)
        {
            return BitConverter.DoubleToInt64Bits(value) < 0 ? "-0x0p+0" : "0x0p+0";
        }

        // Convert double to IEEE 754 representation
        var bits = BitConverter.DoubleToInt64Bits(value);

        // sign bit 
        var isNegative = (bits & (1L << 63)) != 0;

        // 11 bits of exponent
        var exponent = (int)((bits >> 52) & ((1L << 11) - 1));

        // 52 bits of mantissa
        var mantissa = bits & ((1L << 52) - 1);

        var sign = isNegative ? "-" : "";

        if (exponent == 0)
        {
            var leadingZeros = CountLeadingZeros(mantissa, 52);
            mantissa <<= leadingZeros + 1;
            mantissa &= (1L << 52) - 1; // 52ビットにマスク

            var adjustedExponent = -1022 - leadingZeros;

            var mantissaHex = FormatMantissa(mantissa);
            return $"{sign}0x0.{mantissaHex}p{adjustedExponent:+0;-0}";
        }
        else
        {
            var adjustedExponent = exponent - 1023;
            var mantissaHex = FormatMantissa(mantissa);

            if (mantissa == 0)
            {
                return $"{sign}0x1p{adjustedExponent:+0;-0}";
            }
            else
            {
                return $"{sign}0x1.{mantissaHex}p{adjustedExponent:+0;-0}";
            }
        }

        static string FormatMantissa(long mantissa)
        {
            if (mantissa == 0)
            {
                return "";
            }

            var hex = mantissa.ToString("x13"); // 13桁の16進数

            hex = hex.TrimEnd('0');

            return hex;
        }

        static int CountLeadingZeros(long value, int bitLength)
        {
            if (value == 0)
            {
                return bitLength;
            }

            var count = 0;
            var mask = 1L << (bitLength - 1);

            while ((value & mask) == 0 && count < bitLength)
            {
                count++;
                mask >>= 1;
            }

            return count;
        }
    }
}