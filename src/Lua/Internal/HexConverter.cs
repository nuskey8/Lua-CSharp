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
            using var buffer = new PooledArray<char>(text.Length + 1);
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
        for (int i = 0; i < decimalPart.Length; i++)
        {
            decimalValue += ToInt(decimalPart[i]) * Math.Pow(16, -(i + 1));
        }

        double result = value + decimalValue;

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
                    return (c - '0');
                case >= 'A' and <= 'F':
                    return (c - 'A' + 10);
                case >= 'a' and <= 'f':
                    return (c - 'a' + 10);
            }
        }
        return 0;
    }
}