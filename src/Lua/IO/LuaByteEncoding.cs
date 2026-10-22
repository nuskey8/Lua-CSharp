using System.Text;

namespace Lua.IO;

/// <summary>
/// Maps one byte to one <see cref="char"/> (0x00-0xFF) and back, so that a Lua string can carry
/// arbitrary bytes (for example UTF-8 text) with the same <c>#s</c>, <c>string.sub</c> and
/// <c>string.byte</c> results as the reference C implementation.
/// Characters above 0xFF cannot be represented and are written as '?'.
/// </summary>
sealed class LuaByteEncoding : Encoding
{
    public static LuaByteEncoding Instance { get; } = new();

    public override int GetByteCount(char[] chars, int index, int count) => count;

    public override int GetByteCount(string s) => s.Length;

    public override int GetByteCount(ReadOnlySpan<char> chars) => chars.Length;

    public override int GetBytes(
        char[] chars,
        int charIndex,
        int charCount,
        byte[] bytes,
        int byteIndex
    ) => GetBytes(chars.AsSpan(charIndex, charCount), bytes.AsSpan(byteIndex));

    public override int GetBytes(ReadOnlySpan<char> chars, Span<byte> bytes)
    {
        if (bytes.Length < chars.Length)
        {
            throw new ArgumentException("Destination is too small.", nameof(bytes));
        }

        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            bytes[i] = c <= 0xFF ? (byte)c : (byte)'?';
        }

        return chars.Length;
    }

    public override int GetCharCount(byte[] bytes, int index, int count) => count;

    public override int GetCharCount(ReadOnlySpan<byte> bytes) => bytes.Length;

    public override int GetChars(
        byte[] bytes,
        int byteIndex,
        int byteCount,
        char[] chars,
        int charIndex
    ) => GetChars(bytes.AsSpan(byteIndex, byteCount), chars.AsSpan(charIndex));

    public override int GetChars(ReadOnlySpan<byte> bytes, Span<char> chars)
    {
        if (chars.Length < bytes.Length)
        {
            throw new ArgumentException("Destination is too small.", nameof(chars));
        }

        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i] = (char)bytes[i];
        }

        return bytes.Length;
    }

    public override string GetString(byte[] bytes, int index, int count) =>
        string.Create(
            count,
            (bytes, index),
            static (span, state) =>
            {
                for (var i = 0; i < span.Length; i++)
                {
                    span[i] = (char)state.bytes[state.index + i];
                }
            }
        );

    public override int GetMaxByteCount(int charCount) => charCount;

    public override int GetMaxCharCount(int byteCount) => byteCount;
}
