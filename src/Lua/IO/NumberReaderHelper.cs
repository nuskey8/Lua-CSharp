using System.Globalization;
using System.Runtime.CompilerServices;
using Lua.Internal;

namespace Lua.IO;

internal static class NumberReaderHelper
{
    /// <summary>
    /// Scans a numeric input item. Parsing validates incomplete signs,
    /// hexadecimal prefixes and exponents.
    /// </summary>
    /// <param name="span">The span to scan</param>
    /// <returns>The number of characters consumed before the first nonnumeric character</returns>
    public static int ScanNumberLength(ReadOnlySpan<char> span)
    {
        Scanner scanner = new();
        var position = 0;
        while (position < span.Length && scanner.TryConsume(span[position]))
            position++;
        return position;
    }

    internal struct Scanner
    {
        enum Part : byte
        {
            Start,
            Sign,
            Zero,
            Integer,
            Fraction,
            Exponent,
            ExponentDigits,
        }

        Part part;
        bool hex;
        bool hasDigits;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryConsume(char c)
        {
            if (c is >= '0' and <= '9')
            {
                if (part is Part.Start or Part.Sign)
                    part = c == '0' ? Part.Zero : Part.Integer;
                else if (part == Part.Zero)
                    part = Part.Integer;
                else if (part == Part.Exponent)
                    part = Part.ExponentDigits;
                hasDigits = true;
                return true;
            }
            if (part == Part.Start)
            {
                part = Part.Sign;
                if (c is '+' or '-')
                    return true;
            }
            if (part == Part.Sign)
            {
                part = Part.Integer;
            }
            else if (part == Part.Zero)
            {
                part = Part.Integer;
                if (c is 'x' or 'X')
                {
                    hex = true;
                    hasDigits = false;
                    return true;
                }
            }

            if (part == Part.Exponent)
            {
                part = Part.ExponentDigits;
                if (c is '+' or '-')
                    return true;
            }
            if (part == Part.ExponentDigits)
                return false;
            if (hex && c is >= 'a' and <= 'f' or >= 'A' and <= 'F')
            {
                hasDigits = true;
                return true;
            }
            if (part == Part.Integer && c == '.')
            {
                part = Part.Fraction;
                return true;
            }
            if (hasDigits && (hex ? c is 'p' or 'P' : c is 'e' or 'E'))
            {
                part = Part.Exponent;
                return true;
            }
            return false;
        }
    }

    public static bool TryParseToDouble(ReadOnlySpan<char> span, out double result)
    {
        span = span.Trim();
        if (span.Length == 0)
        {
            result = default!;
            return false;
        }

        var sign = 1;
        var first = span[0];
        if (first is '+')
        {
            sign = 1;
            span = span[1..];
        }
        else if (first is '-')
        {
            sign = -1;
            span = span[1..];
        }

        if (span.Length > 2 && span[0] is '0' && span[1] is 'x' or 'X')
        {
            // TODO: optimize
            try
            {
                var d = HexConverter.ToDouble(span) * sign;
                result = d;
                return true;
            }
            catch (FormatException)
            {
                result = default!;
                return false;
            }
        }
        else
        {
            if (double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            {
                result *= sign;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Parses a number from a span and returns the result or null if parsing fails.
    /// </summary>
    /// <param name="span">The span containing the number</param>
    /// <returns>The parsed number or null if parsing failed</returns>
    public static double? ParseNumber(ReadOnlySpan<char> span)
    {
        return TryParseToDouble(span, out var result) ? result : null;
    }
}
