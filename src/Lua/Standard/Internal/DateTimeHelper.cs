using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Lua.Internal.CompilerServices;
using Lua.Runtime;

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
        var ts = TimeSpan.FromSeconds(Math.Truncate(unixTime));
        return DateTime.UnixEpoch + ts;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetUnixTimeFromLocalTime(DateTime dateTime, TimeZoneInfo zone, bool isDst)
    {
        var offset = zone.GetUtcOffset(dateTime);
        // GetUtcOffset already selects standard time for an ambiguous clock reading.
        if (isDst && zone.IsAmbiguousTime(dateTime))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(dateTime);
            var first = new DateTimeOffset(dateTime, offsets[0]);
            offset = zone.IsDaylightSavingTime(first) ? offsets[0] : offsets[1];
        }
        return new DateTimeOffset(dateTime, offset).ToUnixTimeSeconds();
    }

    public static LuaValue GetTime(LuaState state, LuaTable table)
    {
        var sec = GetTimeField(state, table["sec"], "sec", 0);
        var min = GetTimeField(state, table["min"], "min", 0);
        var hour = GetTimeField(state, table["hour"], "hour", 12);
        var day = GetTimeField(state, table["day"], "day");
        var month = GetTimeField(state, table["month"], "month");
        var year = GetTimeField(state, table["year"], "year");
        var zone = state.Platform.TimeProvider.LocalTimeZone;
        return CreateTime(
            zone,
            sec,
            min,
            hour,
            day,
            month,
            year,
            zone.SupportsDaylightSavingTime && table["isdst"].ToBoolean()
        );
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<LuaValue> GetTimeAsync(
        LuaState state,
        LuaTable table,
        CancellationToken cancellationToken
    )
    {
        var sec = GetTimeField(
            state,
            await ReadFieldAsync(state, table, "sec", cancellationToken),
            "sec",
            0
        );
        var min = GetTimeField(
            state,
            await ReadFieldAsync(state, table, "min", cancellationToken),
            "min",
            0
        );
        var hour = GetTimeField(
            state,
            await ReadFieldAsync(state, table, "hour", cancellationToken),
            "hour",
            12
        );
        var day = GetTimeField(
            state,
            await ReadFieldAsync(state, table, "day", cancellationToken),
            "day"
        );
        var month = GetTimeField(
            state,
            await ReadFieldAsync(state, table, "month", cancellationToken),
            "month"
        );
        var year = GetTimeField(
            state,
            await ReadFieldAsync(state, table, "year", cancellationToken),
            "year"
        );
        var isDst = (await ReadFieldAsync(state, table, "isdst", cancellationToken)).ToBoolean();
        return CreateTime(
            state.Platform.TimeProvider.LocalTimeZone,
            sec,
            min,
            hour,
            day,
            month,
            year,
            isDst
        );
    }

    static LuaValue CreateTime(
        TimeZoneInfo zone,
        int sec,
        int min,
        int hour,
        int day,
        int month,
        int year,
        bool isDst
    )
    {
        try
        {
            var normalizedYear = Math.DivRem(
                (long)year * 12 + month - 1,
                12,
                out var normalizedMonth
            );
            if (normalizedMonth < 0)
            {
                normalizedMonth += 12;
                normalizedYear--;
            }

            var date = new DateTime(checked((int)normalizedYear), (int)normalizedMonth + 1, 1);
            date = date.AddSeconds(((long)day - 1) * 86400 + hour * 3600L + min * 60L + sec);
            var time = GetUnixTimeFromLocalTime(date, zone, isDst);
            return time == -1 ? LuaValue.Nil : time;
        }
        catch (ArgumentOutOfRangeException)
        {
            return LuaValue.Nil;
        }
        catch (OverflowException)
        {
            return LuaValue.Nil;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int GetTimeField(LuaState state, LuaValue value, string key, int defaultValue = -1)
    {
        if (value.Type != LuaValueType.Nil && value.TryReadDouble(out var number))
            return (int)(long)number;
        if (defaultValue >= 0)
            return defaultValue;
        throw new LuaRuntimeException(state, $"field '{key}' missing in date table");
    }

    static ValueTask<LuaValue> ReadFieldAsync(
        LuaState state,
        LuaTable table,
        string key,
        CancellationToken cancellationToken
    )
    {
        var value = table[key];
        if (value.Type != LuaValueType.Nil || table.Metatable is null)
            return new(value);
        return LuaVirtualMachine.ExecuteGetTableSlowPath(state, table, key, cancellationToken);
    }

    public static string StrFTime(
        LuaState state,
        ReadOnlySpan<char> format,
        DateTimeOffset time,
        TimeZoneInfo zone
    )
    {
        var d = time.DateTime;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ThrowInvalidConversionSpecifier(LuaState state, ReadOnlySpan<char> format)
        {
            throw new LuaRuntimeException(
                state,
                $"bad argument #1 to 'date' (invalid conversion specifier '{format.ToString()}')"
            );
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
                'd' => "dd",
                'D' => "MM/dd/yy",
                'F' => "yyyy-MM-dd",
                'h' => "MMM",
                'H' => "HH",
                'I' => "hh",
                'm' => "MM",
                'M' => "mm",
                'p' => "tt",
                'r' => "hh:mm:ss tt",
                'R' => "HH:mm",
                'S' => "ss",
                'T' => "HH:mm:ss",
                'y' => "yy",
                'Y' => "yyyy",
                'x' => "MM/dd/yy",
                'X' => "HH:mm:ss",
                _ => null,
            };
        }

        using ValueStringBuilder builder = new();
        Span<char> formatBuffer = stackalloc char[32];

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
                var conversions = c == 'E' ? "cCxXyY" : "deHImMSuUVwWy";
                if (++i >= format.Length || conversions.IndexOf(format[i]) < 0)
                    ThrowInvalidConversionSpecifier(state, format);
                c = format[i];
            }

            isEscapeSequence = false;

            var pattern = STANDARD_PATTERNS(c);
            if (pattern != null)
            {
                d.TryFormat(formatBuffer, out var written, pattern, CultureInfo.InvariantCulture);
                builder.Append(formatBuffer[..written]);
            }
            else
                switch (c)
                {
                    case 'c':
                        d.TryFormat(
                            formatBuffer,
                            out var written,
                            "ddd MMM dd HH:mm:ss yyyy",
                            CultureInfo.InvariantCulture
                        );
                        // C-locale %c space-pads a single-digit day.
                        if (d.Day < 10)
                            formatBuffer[8] = ' ';
                        builder.Append(formatBuffer[..written]);
                        break;
                    case 'e':
                    {
                        var s = d.ToString("%d", CultureInfo.InvariantCulture);
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
                        builder.Append((d.Year / 100).ToString("00", CultureInfo.InvariantCulture));
                        break;
                    case 'j':
                        builder.Append(d.DayOfYear.ToString("000", CultureInfo.InvariantCulture));
                        break;
                    case 'u':
                    {
                        var weekDay = (int)d.DayOfWeek;
                        if (weekDay == 0)
                        {
                            weekDay = 7;
                        }

                        builder.Append(weekDay.ToString(CultureInfo.InvariantCulture));
                        break;
                    }
                    case 'w':
                    {
                        var weekDay = (int)d.DayOfWeek;
                        builder.Append(weekDay.ToString(CultureInfo.InvariantCulture));
                        break;
                    }
                    case 'U':
                    case 'W':
                    {
                        var firstDay = c == 'U' ? 0 : 1;
                        var weekDay = ((int)d.DayOfWeek + 7 - firstDay) % 7;
                        var week = (d.DayOfYear + 6 - weekDay) / 7;
                        builder.Append(week.ToString("00", CultureInfo.InvariantCulture));
                        break;
                    }
                    case 'V':
                        builder.Append(
                            ISOWeek.GetWeekOfYear(d).ToString("00", CultureInfo.InvariantCulture)
                        );
                        break;
                    case 'g':
                    case 'G':
                    {
                        var year = ISOWeek.GetYear(d);
                        builder.Append(
                            (c == 'g' ? year % 100 : year).ToString(
                                c == 'g' ? "00" : "0000",
                                CultureInfo.InvariantCulture
                            )
                        );
                        break;
                    }
                    case 'z':
                        builder.Append(time.Offset < TimeSpan.Zero ? '-' : '+');
                        var offset = time.Offset.Duration();
                        builder.Append(offset.Hours.ToString("00", CultureInfo.InvariantCulture));
                        builder.Append(offset.Minutes.ToString("00", CultureInfo.InvariantCulture));
                        break;
                    case 'Z':
                        builder.Append(
                            state.Platform.EncodeText(
                                zone == TimeZoneInfo.Utc ? "UTC"
                                : zone.IsDaylightSavingTime(time) ? zone.DaylightName
                                : zone.StandardName
                            )
                        );
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

        return builder.AsSpan().ToString();
    }
}
