using System.Globalization;

namespace Lua.Internal;

static class NumberFormatter
{
    public static string Format(double value, LuaNumberFormat format)
    {
        if (format == LuaNumberFormat.Lua && !MathEx.IsFinite(value))
            return FormatSpecial(value, format);
        return value.ToString(
            format == LuaNumberFormat.Lua ? "g14" : null,
            CultureInfo.InvariantCulture
        );
    }

    public static bool TryFormat(
        double value,
        Span<char> destination,
        out int written,
        LuaNumberFormat format
    )
    {
        if (format == LuaNumberFormat.Lua && !MathEx.IsFinite(value))
        {
            var text = FormatSpecial(value, format);
            written = text.Length;
            if (text.AsSpan().TryCopyTo(destination))
                return true;
            written = 0;
            return false;
        }
        return value.TryFormat(
            destination,
            out written,
            format == LuaNumberFormat.Lua ? "g14" : default,
            CultureInfo.InvariantCulture
        );
    }

    public static string FormatSpecial(double value, LuaNumberFormat format, bool upperCase = false)
    {
        string text;
        if (format == LuaNumberFormat.Lua)
        {
            var negative = BitConverter.DoubleToInt64Bits(value) < 0;
            text = double.IsNaN(value) ? (negative ? "-nan" : "nan") : (negative ? "-inf" : "inf");
        }
        else
            text = value.ToString(CultureInfo.InvariantCulture);
        return upperCase ? text.ToUpperInvariant() : text;
    }
}
