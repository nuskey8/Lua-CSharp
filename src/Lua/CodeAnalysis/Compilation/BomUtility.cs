using System.Text;

namespace Lua.CodeAnalysis.Compilation;

static class BomUtility
{
    static ReadOnlySpan<byte> BomUtf8 => [0xEF, 0xBB, 0xBF];

    static ReadOnlySpan<byte> BomUtf16Little => [0xFF, 0xFE];

    static ReadOnlySpan<byte> BomUtf16Big => [0xFE, 0xFF];

    static ReadOnlySpan<byte> BomUtf32Little => [0xFF, 0xFE, 0x00, 0x00];

    /// <summary>
    ///  Removes the BOM from the beginning of the text and returns the encoding.
    ///  Supported encodings are UTF-8, UTF-16 (little and big endian), and UTF-32 (little endian).
    ///  Unknown BOMs are ignored, and the encoding is set to UTF-8 by default.
    ///  </summary>
    ///  <param name="text">The text to check for BOM.</param>
    ///  <param name="encoding">The encoding of the text.</param>
    ///  <returns>The text without the BOM.</returns>
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

        encoding = Encoding.UTF8;
        return text;
    }
}