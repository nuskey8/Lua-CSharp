using System.Buffers;
using System.Text;
using Lua.CodeAnalysis.Compilation;
using Lua.IO;

namespace Lua.Internal;

sealed class Utf8Reader
{
    byte[] buffer = ArrayPool<byte>.Shared.Rent(1024);
    readonly Encoding encoding;
    readonly bool byteStrings;
    int bufPos,
        bufLen;
    Decoder? decoder;

    /// <param name="stringMode">
    /// In byte mode, every byte is decoded to one char (0x00-0xFF) and a UTF-8 BOM is not skipped,
    /// like the reference C implementation. Otherwise the stream is decoded as UTF-8.
    /// </param>
    public Utf8Reader(LuaStringMode stringMode = LuaStringMode.Utf16)
    {
        byteStrings = stringMode == LuaStringMode.Bytes;
        encoding = byteStrings ? LuaByteEncoding.Instance : Encoding.UTF8;
    }

    public long Remain => bufLen - bufPos;

    bool ReadTextBuffer(Stream stream)
    {
        bufLen = stream.Read(buffer, 0, buffer.Length);
        bufPos = !byteStrings && buffer.AsSpan(0, bufLen).StartsWith(BomUtility.BomUtf8) ? 3 : 0;
        return bufPos < bufLen;
    }

    public string? ReadLine(Stream stream, bool keepEol = false)
    {
        using PooledList<byte> result = new();
        while (true)
        {
            if (bufPos >= bufLen && !ReadTextBuffer(stream))
                break;

            var span = buffer.AsSpan(bufPos, bufLen - bufPos);
            // Raw Lua byte streams end a line at LF; CR remains part of the data.
            var end = byteStrings
                ? span.IndexOf((byte)'\n')
                : span.IndexOfAny((byte)'\r', (byte)'\n');
            if (end < 0)
            {
                result.AddRange(span);
                bufPos = bufLen;
                continue;
            }

            var count = end + (keepEol ? 1 : 0);
            bufPos += end + 1;
            if (span[end] == '\r')
            {
                if (bufPos == bufLen)
                {
                    // Preserve the segment before looking for LF in the next buffer.
                    result.AddRange(span[..count]);
                    if (PeekByte(stream) == '\n')
                    {
                        bufPos++;
                        if (keepEol)
                            result.Add((byte)'\n');
                    }
                    return encoding.GetString(result.AsSpan());
                }
                if (buffer[bufPos] == '\n')
                {
                    bufPos++;
                    if (keepEol)
                        count++;
                }
            }
            if (result.Count == 0)
                return encoding.GetString(span[..count]);
            result.AddRange(span[..count]);
            return encoding.GetString(result.AsSpan());
        }
        return result.Count == 0 ? null : encoding.GetString(result.AsSpan());
    }

    public string ReadToEnd(Stream stream)
    {
        using PooledList<byte> result = new();
        while (true)
        {
            if (bufPos >= bufLen && !ReadTextBuffer(stream))
                break;
            result.AddRange(buffer.AsSpan(bufPos, bufLen - bufPos));
            bufPos = bufLen;
        }
        return encoding.GetString(result.AsSpan());
    }

    public int ReadByte(Stream stream)
    {
        var value = PeekByte(stream);
        if (value >= 0)
            bufPos++;
        return value;
    }

    public void CopyBufferedBytes(IBufferWriter<byte> writer)
    {
        var count = bufLen - bufPos;
        if (count == 0)
            return;
        buffer.AsSpan(bufPos, count).CopyTo(writer.GetSpan(count));
        writer.Advance(count);
        bufPos = bufLen;
    }

    public string? Read(Stream stream, int charCount)
    {
        if (charCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(charCount));
        }

        if (charCount == 0)
        {
            return string.Empty;
        }

        var len = 0;
        var dataRead = false;
        var resultBuffer = ArrayPool<char>.Shared.Rent(charCount);

        try
        {
            while (len < charCount)
            {
                if (bufPos >= bufLen && !ReadTextBuffer(stream))
                    break;

                ReadOnlySpan<byte> byteSpan = new(buffer, bufPos, bufLen - bufPos);
                Span<char> charSpan = new(resultBuffer, len, charCount - len);
                if (byteStrings)
                {
                    var count = Math.Min(byteSpan.Length, charSpan.Length);
                    LuaByteEncoding.Instance.GetChars(byteSpan[..count], charSpan);
                    len += count;
                    bufPos += count;
                    dataRead = true;
                    continue;
                }

                decoder ??= encoding.GetDecoder();
                decoder.Convert(
                    byteSpan,
                    charSpan,
                    false,
                    out var bytesUsed,
                    out var charsUsed,
                    out _
                );

                if (charsUsed > 0)
                {
                    len += charsUsed;
                    dataRead = true;
                }

                bufPos += bytesUsed;
                if (bytesUsed == 0)
                {
                    break;
                }
            }

            if (!dataRead)
            {
                return null;
            }

            return resultBuffer.AsSpan(0, len).ToString();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(resultBuffer);
        }
    }

    static bool IsAsciiSpace(char c) => c == ' ' || (c >= '\t' && c <= '\r');

    public void Clear()
    {
        bufPos = 0;
        bufLen = 0;
    }

    public string? ReadNumber(Stream stream)
    {
        var resultBuffer = ArrayPool<char>.Shared.Rent(64); // Numbers shouldn't be too long
        var len = 0;
        var hasStarted = false;
        var isHex = false;
        var hasDecimal = false;
        var lastWasE = false;

        try
        {
            // Skip leading whitespace
            while (true)
            {
                var b = PeekByte(stream);
                if (b == -1)
                {
                    return null; // EOF
                }

                var c = (char)b;
                if (byteStrings ? !IsAsciiSpace(c) : !char.IsWhiteSpace(c))
                {
                    break;
                }

                ReadByte(stream); // Consume whitespace
            }

            // Check for hex prefix at the start
            if (PeekByte(stream) == '0')
            {
                var nextByte = PeekByte(stream, 1);
                if (nextByte == 'x' || nextByte == 'X')
                {
                    isHex = true;
                    resultBuffer[len++] = '0';
                    ReadByte(stream);
                    resultBuffer[len++] = (char)ReadByte(stream);
                    hasStarted = true;
                }
            }

            // Read number characters
            while (true)
            {
                var b = PeekByte(stream);
                if (b == -1)
                {
                    break; // EOF
                }

                var c = (char)b;
                var shouldConsume = false;

                if (!hasStarted && (c == '+' || c == '-'))
                {
                    shouldConsume = true;
                    hasStarted = true;
                }
                else if (isHex)
                {
                    // Hex digits
                    if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
                    {
                        shouldConsume = true;
                        hasStarted = true;
                    }
                    // Hex decimal point
                    else if (c == '.' && !hasDecimal)
                    {
                        shouldConsume = true;
                        hasDecimal = true;
                    }
                    // Hex exponent (p or P)
                    else if ((c == 'p' || c == 'P') && hasStarted)
                    {
                        shouldConsume = true;
                        lastWasE = true;
                    }
                    // Sign after exponent
                    else if (lastWasE && (c == '+' || c == '-'))
                    {
                        shouldConsume = true;
                        lastWasE = false;
                    }
                }
                else
                {
                    // Decimal digits
                    if (c >= '0' && c <= '9')
                    {
                        shouldConsume = true;
                        hasStarted = true;
                        lastWasE = false;
                    }
                    // Decimal point
                    else if (c == '.' && !hasDecimal)
                    {
                        shouldConsume = true;
                        hasDecimal = true;
                        lastWasE = false;
                    }
                    // Exponent (e or E)
                    else if ((c == 'e' || c == 'E') && hasStarted)
                    {
                        shouldConsume = true;
                        lastWasE = true;
                    }
                    // Sign after exponent
                    else if (lastWasE && (c == '+' || c == '-'))
                    {
                        shouldConsume = true;
                        lastWasE = false;
                    }
                }

                if (shouldConsume)
                {
                    if (len >= resultBuffer.Length)
                    {
                        // Number too long, expand buffer
                        var newBuffer = ArrayPool<char>.Shared.Rent(resultBuffer.Length * 2);
                        resultBuffer.AsSpan(0, len).CopyTo(newBuffer);
                        ArrayPool<char>.Shared.Return(resultBuffer);
                        resultBuffer = newBuffer;
                    }

                    resultBuffer[len++] = c;
                    ReadByte(stream); // Consume the byte
                }
                else
                {
                    break; // Not part of the number
                }
            }

            return len == 0 ? null : resultBuffer.AsSpan(0, len).ToString();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(resultBuffer);
        }
    }

    public bool IsEndOfStream(Stream stream)
    {
        return PeekByte(stream) < 0;
    }

    int PeekByte(Stream stream, int offset = 0)
    {
        // Ensure we have enough data in buffer
        while (bufPos + offset >= bufLen)
        {
            if (bufLen == 0 || bufPos == bufLen)
            {
                bufLen = stream.Read(buffer, 0, buffer.Length);
                bufPos = 0;
                if (bufLen == 0)
                {
                    return -1; // EOF
                }
            }
            else
            {
                // We need more data but buffer has some - this shouldn't happen with small offsets
                return -1;
            }
        }

        return buffer[bufPos + offset];
    }

    public void Dispose()
    {
        var rented = buffer;
        buffer = [];
        bufPos = bufLen = 0;
        ArrayPool<byte>.Shared.Return(rented);
    }
}
