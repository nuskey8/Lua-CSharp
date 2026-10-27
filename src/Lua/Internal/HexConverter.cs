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
        if (expIndex >= 0 && (expIndex == text.Length - 1 || dotIndex > expIndex))
            throw new FormatException("Invalid hexadecimal exponent.");

        ReadOnlySpan<char> intPart;
        ReadOnlySpan<char> decimalPart;
        ReadOnlySpan<char> expPart;

        if (dotIndex == -1)
        {
            intPart = expIndex < 0 ? text : text[..expIndex];
            decimalPart = [];
            expPart = expIndex < 0 ? [] : text[(expIndex + 1)..];
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

        double value = 0;
        if (intPart.Length == 0 && decimalPart.Length == 0)
            throw new FormatException("Hexadecimal number has no digits.");
        foreach (var digit in intPart)
            value = (value * 16) + ToInt(digit);
        for (var i = 0; i < decimalPart.Length; i++)
        {
            value = (value * 16) + ToInt(decimalPart[i]);
        }
        var exponent = -4 * decimalPart.Length;
        if (expPart.Length > 0)
        {
            var negative = expPart[0] == '-';
            if (expPart[0] is '+' or '-')
                expPart = expPart[1..];
            if (expPart.Length == 0)
                throw new FormatException("Invalid hexadecimal exponent.");
            var parsedExponent = 0;
            foreach (var digit in expPart)
            {
                if (digit is < '0' or > '9')
                    throw new FormatException("Invalid hexadecimal exponent.");
                parsedExponent = unchecked((parsedExponent * 10) + digit - '0');
            }
            exponent = unchecked(exponent + (negative ? -parsedExponent : parsedExponent));
        }
        return MathEx.ScaleB(value, exponent) * sign;
    }

    static int ToInt(char c)
    {
        return c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'A' and <= 'F' => c - 'A' + 10,
            >= 'a' and <= 'f' => c - 'a' + 10,
            _ => throw new FormatException("Invalid hexadecimal digit."),
        };
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

        // Subnormals have no implicit leading one and share the exponent -1022.
        var adjustedExponent = exponent == 0 ? -1022 : exponent - 1023;
        if (mantissa == 0)
        {
            return $"{sign}0x1p{adjustedExponent:+0;-0}";
        }

        var leadingDigit = exponent == 0 ? '0' : '1';
        var mantissaHex = mantissa.ToString("x13").TrimEnd('0');
        return $"{sign}0x{leadingDigit}.{mantissaHex}p{adjustedExponent:+0;-0}";
    }
}
