using System.Text;

namespace Lua.Internal;

internal static class BomUtility
{
    static ReadOnlySpan<byte> BomUtf8 => [0xEF, 0xBB, 0xBF];
    static ReadOnlySpan<byte> BomUtf16Little => [0xFF, 0xFE];
    static ReadOnlySpan<byte> BomUtf16Big => [0xFE, 0xFF];
    static ReadOnlySpan<byte> BomUtf32Little => [0xFF, 0xFE, 0x00, 0x00];
    static ReadOnlySpan<byte> BomUtf32Big => [0x00, 0x00, 0xFE, 0xFF];

    public static ReadOnlySpan<byte> GetEncodingFromBytes(ReadOnlySpan<byte> text, out Encoding encoding)
    {
        if (text.StartsWith(BomUtf8))
        {
            encoding = Encoding.UTF8;
            return text.Slice(BomUtf8.Length);
        }

        if (text.StartsWith(BomUtf16Little))
        {
            encoding = Encoding.Unicode;
            return text.Slice(BomUtf16Little.Length);
        }

        if (text.StartsWith(BomUtf16Big))
        {
            encoding = Encoding.BigEndianUnicode;
            return text.Slice(BomUtf16Big.Length);
        }

        if (text.StartsWith(BomUtf32Little))
        {
            encoding = Encoding.UTF32;
            return text.Slice(BomUtf32Little.Length);
        }

        if (text.StartsWith(BomUtf32Big))
        {
            encoding = Encoding.UTF32;
            return text.Slice(BomUtf32Big.Length);
        }

        encoding = Encoding.UTF8;
        return text;
    }
}