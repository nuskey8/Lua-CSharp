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

    public static DateTime ParseTimeTable(LuaState thread, LuaTable table)
    {
        static int GetTimeField(LuaState thread, LuaTable table, string key, bool required = true, int defaultValue = 0)
        {
            if (!table.TryGetValue(key, out var value))
            {
                if (required)
                {
                    throw new LuaRuntimeException(thread, $"field '{key}' missing in date table");
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

            throw new LuaRuntimeException(thread, $"field '{key}' is not an integer");
        }

        var day = GetTimeField(thread, table, "day");
        var month = GetTimeField(thread, table, "month");
        var year = GetTimeField(thread, table, "year");
        var sec = GetTimeField(thread, table, "sec", false, 0);
        var min = GetTimeField(thread, table, "min", false, 0);
        var hour = GetTimeField(thread, table, "hour", false, 12);

        return new(year, month, day, hour, min, sec);
    }

    public static string StrFTime(LuaState thread, ReadOnlySpan<char> format, DateTime d)
    {
        // reference: http://www.cplusplus.com/reference/ctime/strftime/

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

                continue;
            }

            if (!isEscapeSequence)
            {
                builder.Append(c);
                continue;
            }

            if (c == 'O' || c == 'E')
            {
                continue; // no modifiers
            }

            isEscapeSequence = false;

            var pattern = STANDARD_PATTERNS(c);
            if (pattern != null)
            {
                builder.Append(d.ToString(pattern));
            }
            else if (c == 'e')
            {
                var s = d.ToString("%d");
                builder.Append(s.Length < 2 ? $" {s}" : s);
            }
            else if (c == 'n')
            {
                builder.Append('\n');
            }
            else if (c == 't')
            {
                builder.Append('\t');
            }
            else if (c == 'C')
            {
                // TODO: reduce allocation
                builder.Append((d.Year / 100).ToString());
            }
            else if (c == 'j')
            {
                builder.Append(d.DayOfYear.ToString("000"));
            }
            else if (c == 'u')
            {
                var weekDay = (int)d.DayOfWeek;
                if (weekDay == 0)
                {
                    weekDay = 7;
                }

                builder.Append(weekDay.ToString());
            }
            else if (c == 'w')
            {
                var weekDay = (int)d.DayOfWeek;
                builder.Append(weekDay.ToString());
            }
            else if (c == 'U')
            {
                // Week number with the first Sunday as the first day of week one (00-53)
                builder.Append("??");
            }
            else if (c == 'V')
            {
                // ISO 8601 week number (00-53)
                builder.Append("??");
            }
            else if (c == 'W')
            {
                // Week number with the first Monday as the first day of week one (00-53)
                builder.Append("??");
            }
            else
            {
                throw new LuaRuntimeException(thread, $"bad argument #1 to 'date' (invalid conversion specifier '{format.ToString()}')");
            }
        }

        return builder.ToString();
    }
}