using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Lua.Internal;

internal static class StringHelper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<char> Slice(string s, int i, int j)
    {
        if (i < 0) i = s.Length + i + 1;
        if (j < 0) j = s.Length + j + 1;

        if (i < 1) i = 1;
        if (j > s.Length) j = s.Length;

        return i > j ? "" : s.AsSpan()[(i - 1)..j];
    }

    public static bool TryFromStringLiteral(ReadOnlySpan<char> literal, [NotNullWhen(true)] out string? result)
    {
        var builder = new ValueStringBuilder(literal.Length);
        for (int i = 0; i < literal.Length; i++)
        {
            var c = literal[i];
            if (c is '\\' && i < literal.Length - 1)
            {
                i++;
                c = literal[i];

                switch (c)
                {
                    case '\n':
                        builder.Append('\n');
                        break;
                    case '\r':
                        builder.Append('\n');
                        // check CRLF
                        if (i + 1 < literal.Length && literal[i + 1] is '\n')
                        {
                            i++;
                        }
                        break;
                    case 'a':
                        builder.Append('\a');
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'v':
                        builder.Append('\v');
                        break;
                    case '\\':
                        builder.Append('\\');
                        break;
                    case '\"':
                        builder.Append('\"');
                        break;
                    case '\'':
                        builder.Append('\'');
                        break;
                    case '[':
                        builder.Append('[');
                        break;
                    case ']':
                        builder.Append(']');
                        break;
                    case 'x':
                        i++;
                        if (i >= literal.Length)
                        {
                            result = null;
                            return false;
                        }

                        c = literal[i];
                        if (IsDigit(c))
                        {
                            var start = i;
                            for (int j = 0; j < 2; j++)
                            {
                                i++;
                                if (i >= literal.Length) break;
                                c = literal[i];
                                if (!IsDigit(c)) break;
                            }

                            builder.Append((char)int.Parse(literal[start..i], NumberStyles.HexNumber));
                            i--;
                        }
                        else
                        {
                            result = null;
                            return false;
                        }
                        break;
                    default:
                        if (IsNumber(c))
                        {
                            var start = i;
                            for (int j = 0; j < 3; j++)
                            {
                                i++;
                                if (i >= literal.Length) break;
                                c = literal[i];
                                if (!IsNumber(c)) break;
                            }

                            builder.Append((char)int.Parse(literal[start..i]));
                            i--;
                        }
                        else
                        {
                            result = null;
                            return false;
                        }
                        break;
                }
            }
            else
            {
                builder.Append(c);
            }
        }

        result = builder.ToString();
        return true;
    }

    public static string Escape(ReadOnlySpan<char> str)
    {
        var builder = new ValueStringBuilder(str.Length);

        for (int i = 0; i < str.Length; i++)
        {
            var c = str[i];

            switch (c)
            {
                case '\a':
                    builder.Append("\\\a");
                    break;
                case '\b':
                    builder.Append("\\\b");
                    break;
                case '\f':
                    builder.Append("\\\f");
                    break;
                case '\n':
                    builder.Append("\\\n");
                    break;
                case '\r':
                    builder.Append("\\\r");
                    break;
                case '\t':
                    builder.Append("\\\t");
                    break;
                case '\v':
                    builder.Append("\\\v");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\"':
                    builder.Append("\\\"");
                    break;
                case '\'':
                    builder.Append("\\\'");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    public static Regex ToRegex(ReadOnlySpan<char> pattern)
    {
        var builder = new ValueStringBuilder();
        var isEscapeSequence = false;
        var isInSet = false;

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];

            if (isEscapeSequence)
            {
                if (c == '%' || c == '_')
                {
                    builder.Append(c);
                    isEscapeSequence = false;
                }
                else
                {
                    switch (c)
                    {
                        case 'a': // all letters
                            builder.Append("\\p{L}");
                            break;
                        case 'A': // all Non letters
                            builder.Append("\\P{L}");
                            break;
                        case 's': // all space characters
                            builder.Append("\\s");
                            break;
                        case 'S': // all NON space characters
                            builder.Append("\\S");
                            break;
                        case 'd': // all digits
                            builder.Append("\\d");
                            break;
                        case 'D': // all NON digits
                            builder.Append("\\D");
                            break;
                        case 'w': // all alphanumeric characters
                            builder.Append("\\w");
                            break;
                        case 'W': // all NON alphanumeric characters
                            builder.Append("\\W");
                            break;
                        case 'c': // all control characters
                            builder.Append("\\p{C}");
                            break;
                        case 'C': // all NON control characters
                            builder.Append("[\\P{C}]");
                            break;
                        case 'g': // all printable characters except space
                            builder.Append("[^\\p{C}\\s]");
                            break;
                        case 'G': // all NON printable characters including space
                            builder.Append("[\\p{C}\\s]");
                            break;
                        case 'p': // all punctuation characters
                            builder.Append("\\p{P}");
                            break;
                        case 'P': // all NON punctuation characters
                            builder.Append("\\P{P}");
                            break;
                        case 'l': // all lowercase letters
                            builder.Append("\\p{Ll}");
                            break;
                        case 'L': // all NON lowercase letters
                            builder.Append("\\P{Ll}");
                            break;
                        case 'u': // all uppercase letters
                            builder.Append("\\p{Lu}");
                            break;
                        case 'U': // all NON uppercase letters
                            builder.Append("\\P{Lu}");
                            break;
                        case 'x': // all hexadecimal digits
                            builder.Append("[0-9A-Fa-f]");
                            break;
                        case 'X': // all NON hexadecimal digits
                            builder.Append("[^0-9A-Fa-f]");
                            break;
                        case 'b':
                            if (i < pattern.Length - 2)
                            {
                                var c1 = pattern[i + 1];
                                var c2 = pattern[i + 2];

                                var c1Escape = Regex.Escape(c1.ToString());
                                var c2Escape = Regex.Escape(c2.ToString());

                                builder.Append("(");
                                builder.Append(c1Escape);
                                builder.Append("(?>(?<n>");
                                builder.Append(c1Escape);
                                builder.Append(")|(?<-n>");
                                builder.Append(c2Escape);
                                builder.Append(")|(?:[^");
                                builder.Append(c1Escape);
                                builder.Append(c2Escape);
                                builder.Append("]*))*");
                                builder.Append(c2Escape);
                                builder.Append("(?(n)(?!)))");
                                i += 2;
                            }
                            else
                            {
                                throw new Exception(); // TODO: add message
                            }

                            break;
                        default:
                            builder.Append('\\');
                            builder.Append(c);
                            break;
                    }

                    isEscapeSequence = false;
                }
            }
            else if (c == '%')
            {
                isEscapeSequence = true;
            }
            else if (c == '\\')
            {
                builder.Append("\\\\");
            }
            else if (isInSet)
            {
                if (c == ']') isInSet = false;
                builder.Append(c);
            }
            else if (c == '-')
            {
                builder.Append("*?");
            }
            else if (c == '[')
            {
                builder.Append('[');
                isInSet = true;
            }
            else if (c == '^' && !isInSet)
            {
                builder.Append("\\G");
            }
            else if (c == '(')
            {
                builder.Append('(');
            }
            else
            {
                builder.Append(c);
            }
        }

        return new Regex(builder.ToString());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNumber(char c)
    {
        return '0' <= c && c <= '9';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDigit(char c)
    {
        return IsNumber(c) ||
               ('a' <= c && c <= 'f') ||
               ('A' <= c && c <= 'F');
    }
}