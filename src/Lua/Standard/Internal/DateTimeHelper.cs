using System.Runtime.CompilerServices;
using System.Text;

namespace Lua.Standard.Internal;

static class DateTimeHelper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetUnixTime(DateTime dateTime)
    {
        return GetUnixTime(dateTime, DateTime.UnixEpoch);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetUnixTime(DateTime dateTime, DateTime epoch)
    {
        var time = (dateTime - epoch).TotalSeconds;
        if (time < 0.0)
        {
            return 0;
        }

        return time;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTime FromUnixTime(double unixTime)
    {
        var ts = TimeSpan.FromSeconds(unixTime);
        return DateTime.UnixEpoch + ts;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetUnixTimeFromLocalTime(DateTime dateTime)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Local)).ToUnixTimeSeconds();
    }

    public static DateTime ParseTimeTable(LuaState state, LuaTable table)
    {
        static int GetTimeField(LuaState state, LuaTable table, string key, bool required = true, int defaultValue = 0)
        {
            if (!table.TryGetValue(key, out var value))
            {
                if (required)
                {
                    throw new LuaRuntimeException(state, $"field '{key}' missing in date table");
                }
                else
                {
                    return defaultValue;
                }
            }

            if (value.TryRead<double>(out var d) && MathEx.IsInteger(d))
            {
                return (int)d;
            }

            throw new LuaRuntimeException(state, $"field '{key}' is not an integer");
        }

        var day = GetTimeField(state, table, "day");
        var month = GetTimeField(state, table, "month");
        var year = GetTimeField(state, table, "year");
        var sec = GetTimeField(state, table, "sec", false, 0);
        var min = GetTimeField(state, table, "min", false, 0);
        var hour = GetTimeField(state, table, "hour", false, 12);

        return new(year, month, day, hour, min, sec);
    }

    public static string StrFTime(LuaState state, ReadOnlySpan<char> format, DateTime d)
    {
        // reference: http://www.cplusplus.com/reference/ctime/strftime/

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ThrowInvalidConversionSpecifier(LuaState state, ReadOnlySpan<char> format)
        {
            throw new LuaRuntimeException(state, $"bad argument #1 to 'date' (invalid conversion specifier '{format.ToString()}')");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static string? STANDARD_PATTERNS(char c)
        {
            return c switch
            {
                'a' => "ddd",
                'A' => "dddd",
                'b' => "MMM",
                'B' => "MMMM",
                'c' => "f",
                'd' => "dd",
                'D' => "MM/dd/yy",
                'F' => "yyyy-MM-dd",
                'g' => "yy",
                'G' => "yyyy",
                'h' => "MMM",
                'H' => "HH",
                'I' => "hh",
                'm' => "MM",
                'M' => "mm",
                'p' => "tt",
                'r' => "h:mm:ss tt",
                'R' => "HH:mm",
                'S' => "ss",
                'T' => "HH:mm:ss",
                'y' => "yy",
                'Y' => "yyyy",
                'x' => "d",
                'X' => "T",
                'z' => "zzz",
                'Z' => "zzz",
                _ => null
            };
        }

        ValueStringBuilder builder = new();

        var isEscapeSequence = false;

        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];

            if (c == '%')
            {
                if (isEscapeSequence)
                {
                    builder.Append('%');
                    isEscapeSequence = false;
                }
                else
                {
                    isEscapeSequence = true;
                }

                continue;
            }

            if (!isEscapeSequence)
            {
                builder.Append(c);
                continue;
            }

            if (c is 'O' or 'E')
            {
                ThrowInvalidConversionSpecifier(state, format);
            }

            isEscapeSequence = false;

            var pattern = STANDARD_PATTERNS(c);
            if (pattern != null)
            {
                builder.Append(d.ToString(pattern));
            }
            else switch (c)
            {
                case 'e':
                    {
                        var s = d.ToString("%d");
                        builder.Append(s.Length < 2 ? $" {s}" : s);
                        break;
                    }
                case 'n':
                    builder.Append('\n');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'C':
                    // TODO: reduce allocation
                    builder.Append((d.Year / 100).ToString());
                    break;
                case 'j':
                    builder.Append(d.DayOfYear.ToString("000"));
                    break;
                case 'u':
                    {
                        var weekDay = (int)d.DayOfWeek;
                        if (weekDay == 0)
                        {
                            weekDay = 7;
                        }

                        builder.Append(weekDay.ToString());
                        break;
                    }
                case 'w':
                    {
                        var weekDay = (int)d.DayOfWeek;
                        builder.Append(weekDay.ToString());
                        break;
                    }
                case 'U':
                // ISO 8601 week number (00-53)
                case 'V':
                // Week number with the first Monday as the first day of week one (00-53)
                case 'W':
                    // Week number with the first Sunday as the first day of week one (00-53)
                    builder.Append("??");
                    break;
                default:
                    ThrowInvalidConversionSpecifier(state, format);
                    break;
            }
        }

        if (isEscapeSequence)
        {
            ThrowInvalidConversionSpecifier(state, format);
        }

        return builder.ToString();
    }
}
