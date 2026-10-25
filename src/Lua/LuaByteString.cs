using System.Text;
using Lua.Internal;
using Lua.IO;

namespace Lua;

/// <summary>
/// Conversions between .NET strings and Lua byte strings (see
/// <see cref="LuaStringMode.Bytes"/>), where every <see cref="char"/> holds one byte.
/// </summary>
public static class LuaByteString
{
    /// <summary>
    /// Converts raw bytes (for example UTF-8 text) to a byte string.
    /// </summary>
    public static string FromBytes(ReadOnlySpan<byte> bytes)
    {
        return LuaByteEncoding.Instance.GetString(bytes);
    }

    /// <summary>
    /// Converts a byte string back to raw bytes. Chars above 0xFF are written as '?'.
    /// </summary>
    public static byte[] ToBytes(string byteString)
    {
        return LuaByteEncoding.Instance.GetBytes(byteString);
    }

    /// <summary>
    /// Converts a .NET string to a byte string holding its UTF-8 bytes.
    /// Unpaired UTF-16 surrogates are replaced with U+FFFD.
    /// </summary>
    public static string EncodeUtf8(string text)
    {
        var count = Encoding.UTF8.GetByteCount(text);
        if (count == text.Length)
        {
            return text; // ASCII already has the byte-string representation.
        }
        return string.Create(
            count,
            text,
            static (destination, source) => Utf8CharEncoder.Encode(source, destination)
        );
    }

    /// <summary>
    /// Decodes the UTF-8 bytes held by a byte string into a .NET string.
    /// Invalid UTF-8 is replaced with U+FFFD.
    /// </summary>
    public static string DecodeUtf8(string byteString)
    {
        return Encoding.UTF8.GetString(ToBytes(byteString));
    }
}
