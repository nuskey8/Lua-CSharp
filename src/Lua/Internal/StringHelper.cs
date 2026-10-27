using System.Runtime.CompilerServices;
using System.Text;

namespace Lua.Internal;

static class StringHelper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<char> Slice(string s, int i, int j)
    {
        i = RelativePosition(i, s.Length);
        j = RelativePosition(j, s.Length);

        if (i < 1)
            i = 1;
        if (j > s.Length)
            j = s.Length;

        return i > j ? "" : s.AsSpan()[(i - 1)..j];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int RelativePosition(int index, int length) =>
        index < 0 ? length + index + 1 : index;

    public static string Quote(ReadOnlySpan<char> str)
    {
        ValueStringBuilder builder = new(str.Length + 2);
        builder.Append('"');

        for (var i = 0; i < str.Length; i++)
        {
            var c = str[i];

            if (c is '"' or '\\' or '\n')
            {
                builder.Append('\\');
                builder.Append(c);
            }
            else if (c < 32 || c == 127)
            {
                // A following digit must not become part of this decimal escape.
                var pad = i + 1 < str.Length && IsNumber(str[i + 1]);
                builder.Append('\\');
                if (c >= 100 || pad)
                    builder.Append((char)('0' + c / 100));
                if (c >= 10 || pad)
                    builder.Append((char)('0' + c / 10 % 10));
                builder.Append((char)('0' + c % 10));
            }
            else
            {
                builder.Append(c);
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>
    /// toupper in the C locale: only 'a'-'z' change, every other char (including bytes 0x80-0xFF) is kept.
    /// </summary>
    public static string ToUpperAscii(string s)
    {
        var i = 0;
        while (i < s.Length && !(s[i] >= 'a' && s[i] <= 'z'))
        {
            i++;
        }

        if (i == s.Length)
        {
            return s;
        }

        return string.Create(
            s.Length,
            s,
            static (span, source) =>
            {
                for (var k = 0; k < span.Length; k++)
                {
                    var c = source[k];
                    span[k] = c >= 'a' && c <= 'z' ? (char)(c - 32) : c;
                }
            }
        );
    }

    /// <summary>
    /// tolower in the C locale: only 'A'-'Z' change, every other char (including bytes 0x80-0xFF) is kept.
    /// </summary>
    public static string ToLowerAscii(string s)
    {
        var i = 0;
        while (i < s.Length && !(s[i] >= 'A' && s[i] <= 'Z'))
        {
            i++;
        }

        if (i == s.Length)
        {
            return s;
        }

        return string.Create(
            s.Length,
            s,
            static (span, source) =>
            {
                for (var k = 0; k < span.Length; k++)
                {
                    var c = source[k];
                    span[k] = c >= 'A' && c <= 'Z' ? (char)(c + 32) : c;
                }
            }
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNumber(char c)
    {
        return '0' <= c && c <= '9';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDigit(char c)
    {
        return IsNumber(c) || ('a' <= c && c <= 'f') || ('A' <= c && c <= 'F');
    }
}
