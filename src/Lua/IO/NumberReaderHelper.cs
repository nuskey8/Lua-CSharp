using Lua.Internal;
using System.Globalization;

namespace Lua.IO;

internal static class NumberReaderHelper
{
    /// <summary>
    /// Scans a span of characters to find the extent of a valid number.
    /// Returns the length of the number portion, or 0 if no valid number is found.
    /// </summary>
    /// <param name="span">The span to scan</param>
    /// <param name="skipWhitespace">Whether to skip leading whitespace</param>
    /// <returns>The length of the valid number portion</returns>
    public static int ScanNumberLength(ReadOnlySpan<char> span, bool skipWhitespace = true)
    {
        var position = 0;

        // Skip leading whitespace
        if (skipWhitespace)
        {
            while (position < span.Length && char.IsWhiteSpace(span[position]))
            {
                position++;
            }
        }

        if (position >= span.Length)
        {
            return 0;
        }

        var numberStart = position;
        var hasStarted = false;
        var isHex = false;
        var hasDecimal = false;
        var lastWasE = false;

        // Check for sign
        if (position < span.Length && (span[position] == '+' || span[position] == '-'))
        {
            position++;
            hasStarted = true;
        }

        // Check for hex prefix right at the start (after optional sign)
        if (position < span.Length - 1 && span[position] == '0' && (span[position + 1] == 'x' || span[position + 1] == 'X'))
        {
            isHex = true;
            position += 2; // Skip '0x' or '0X'
            hasStarted = true;
        }

        // Scan for valid number characters
        while (position < span.Length)
        {
            var c = span[position];

            // Hex prefix is handled above before the loop

            if (isHex)
            {
                // Hex digits
                if (c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F')
                {
                    position++;
                    hasStarted = true;
                }
                // Hex decimal point
                else if (c == '.' && !hasDecimal)
                {
                    position++;
                    hasDecimal = true;
                }
                // Hex exponent (p or P)
                else if (c is 'p' or 'P' && hasStarted)
                {
                    position++;
                    lastWasE = true;
                }
                // Sign after exponent
                else if (lastWasE && c is '+' or '-')
                {
                    position++;
                    lastWasE = false;
                }
                else
                {
                    break;
                }
            }
            else
            {
                // Decimal digits
                if (c is >= '0' and <= '9')
                {
                    position++;
                    hasStarted = true;
                    lastWasE = false;
                }
                // Decimal point
                else if (c == '.' && !hasDecimal)
                {
                    position++;
                    hasDecimal = true;
                    lastWasE = false;
                }
                // Exponent (e or E)
                else if (c is 'e' or 'E' && hasStarted)
                {
                    position++;
                    lastWasE = true;
                }
                // Sign after exponent
                else if (lastWasE && c is '+' or '-')
                {
                    position++;
                    lastWasE = false;
                }
                else
                {
                    break;
                }
            }
        }

        // Return the length of the number portion
        return position - numberStart;
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
            return double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
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