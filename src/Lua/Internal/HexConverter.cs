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

    internal static string FormatDouble(
        double value,
        int precision,
        bool alternateForm,
        bool upperCase
    )
    {
        string text;
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            text = value.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            text = FormatFinite(value, precision, alternateForm);
        }
        return upperCase ? text.ToUpperInvariant() : text;
    }

    static string FormatFinite(double value, int precision, bool alternateForm)
    {
        const ulong fractionMask = (1UL << 52) - 1;
        var bits = BitConverter.DoubleToInt64Bits(value);
        var exponent = (int)((bits >> 52) & 0x7ff);
        var significand = ((ulong)bits & fractionMask) | (exponent == 0 ? 0 : 1UL << 52);
        var adjustedExponent = exponent == 0 ? (significand == 0 ? 0 : -1022) : exponent - 1023;

        if (precision is >= 0 and < 13)
        {
            var shift = 52 - precision * 4;
            var retained = significand >> shift;
            var discarded = significand & ((1UL << shift) - 1);
            var halfway = 1UL << (shift - 1);
            // Round to nearest, with ties to an even final hexadecimal digit.
            if (discarded > halfway || (discarded == halfway && (retained & 1) != 0))
                retained++;
            significand = retained << shift;
        }

        var sign = bits < 0 ? "-" : "";
        var leadingDigit = (char)('0' + (significand >> 52));
        var fraction = significand & fractionMask;
        var digits = "";
        if (precision != 0 && (precision >= 0 || fraction != 0))
        {
            digits = fraction.ToString("x13", CultureInfo.InvariantCulture);
            digits =
                precision < 0 ? digits.TrimEnd('0')
                : precision < 13 ? digits[..precision]
                : digits.PadRight(precision, '0');
        }
#if NET6_0_OR_GREATER
        if (digits.Length == 0 && !alternateForm)
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{sign}0x{leadingDigit}p{adjustedExponent:+0;-0}"
            );
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sign}0x{leadingDigit}.{digits}p{adjustedExponent:+0;-0}"
        );
#else
        if (digits.Length == 0 && !alternateForm)
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}0x{1}p{2:+0;-0}",
                sign,
                leadingDigit,
                adjustedExponent
            );
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}0x{1}.{2}p{3:+0;-0}",
            sign,
            leadingDigit,
            digits,
            adjustedExponent
        );
#endif
    }
}
