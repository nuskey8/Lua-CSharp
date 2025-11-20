using Lua.CodeAnalysis.Compilation;
using System.Buffers;
using System.Text;

namespace Lua.Internal;

sealed class Utf8Reader
{
    [ThreadStatic]
    static byte[]? scratchBuffer;

    [ThreadStatic]
    internal static bool scratchBufferUsed;

    readonly byte[] buffer;
    int bufPos, bufLen;
    Decoder? decoder;

    const int ThreadStaticBufferSize = 1024;

    public Utf8Reader()
    {
        if (scratchBufferUsed)
        {
            buffer = new byte[ThreadStaticBufferSize];
            return;
        }

        scratchBuffer ??= new byte[ThreadStaticBufferSize];

        buffer = scratchBuffer;
        scratchBufferUsed = true;
    }

    public long Remain => bufLen - bufPos;

    public string? ReadLine(Stream stream, bool keepEol = false)
    {
        var resultBuffer = ArrayPool<byte>.Shared.Rent(1024);
        var lineLen = 0;
        try
        {
            while (true)
            {
                if (bufPos >= bufLen)
                {
                    bufLen = stream.Read(buffer, 0, buffer.Length);
                    bufPos = buffer.AsSpan().StartsWith(BomUtility.BomUtf8) ? 3 : 0;
                    if (bufLen <= bufPos)
                    {
                        break; // EOF
                    }
                }

                Span<byte> span = new(buffer, bufPos, bufLen - bufPos);
                var idx = span.IndexOfAny((byte)'\r', (byte)'\n');

                if (idx >= 0)
                {
                    // Add the line content (before the newline)
                    AppendToBuffer(ref resultBuffer, span[..idx], ref lineLen);

                    var nl = span[idx];
                    var eolStart = bufPos + idx;
                    bufPos += idx + 1;

                    // Handle CRLF - check if we have \r\n
                    var isCRLF = false;
                    if (nl == (byte)'\r' && bufPos < bufLen && buffer[bufPos] == (byte)'\n')
                    {
                        isCRLF = true;
                        bufPos++; // Skip the \n as well
                    }

                    // Add end-of-line characters if keepEol is true
                    if (keepEol)
                    {
                        if (isCRLF)
                        {
                            // Add \r\n
                            AppendToBuffer(ref resultBuffer, "\r\n"u8, ref lineLen);
                        }
                        else
                        {
                            // Add just the single newline character (\r or \n)
                            AppendToBuffer(ref resultBuffer, nl == '\r' ? "\r"u8 : "\n"u8, ref lineLen);
                        }
                    }

                    return Encoding.UTF8.GetString(resultBuffer, 0, lineLen);
                }
                else
                {
                    // No newline found → add all to line buffer
                    AppendToBuffer(ref resultBuffer, span, ref lineLen);
                    bufPos = bufLen;
                }
            }

            if (lineLen == 0)
            {
                return null;
            }

            return Encoding.UTF8.GetString(resultBuffer, 0, lineLen);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(resultBuffer);
        }
    }

    public string ReadToEnd(Stream stream)
    {
        var resultBuffer = ArrayPool<byte>.Shared.Rent(1024);
        var len = 0;
        try
        {
            while (true)
            {
                if (bufPos >= bufLen)
                {
                    bufLen = stream.Read(buffer, 0, buffer.Length);
                    bufPos = buffer.AsSpan().StartsWith(BomUtility.BomUtf8) ? 3 : 0;
                    if (bufLen <= bufPos)
                    {
                        break; // EOF
                    }
                }

                Span<byte> span = new(buffer, bufPos, bufLen - bufPos);
                AppendToBuffer(ref resultBuffer, span, ref len);
                bufPos = bufLen;
            }

            if (len == 0)
            {
                return "";
            }

            return Encoding.UTF8.GetString(resultBuffer, 0, len);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(resultBuffer);
        }
    }

    public byte ReadByte(Stream stream)
    {
        if (buffer.Length == 0)
        {
            return 0;
        }

        var len = 0;
        while (len < 1)
        {
            if (bufPos >= bufLen)
            {
                bufLen = stream.Read(buffer, 0, buffer.Length);
                bufPos = 0;
                if (bufLen == 0)
                {
                    break; // EOF
                }
            }

            var bytesToRead = Math.Min(1, bufLen - bufPos);
            if (bytesToRead == 0)
            {
                break;
            }

            if (bytesToRead > 0)
            {
                len += bytesToRead;
            }
        }

        return buffer[bufPos++];
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
                if (bufPos >= bufLen)
                {
                    bufLen = stream.Read(buffer, 0, buffer.Length);
                    bufPos = buffer.AsSpan().StartsWith(BomUtility.BomUtf8) ? 3 : 0;
                    if (bufLen <= bufPos)
                    {
                        break; // EOF
                    }
                }

                ReadOnlySpan<byte> byteSpan = new(buffer, bufPos, bufLen - bufPos);
                Span<char> charSpan = new(resultBuffer, len, charCount - len);
                decoder ??= Encoding.UTF8.GetDecoder();
                decoder.Convert(
                    byteSpan,
                    charSpan,
                    false,
                    out var bytesUsed,
                    out var charsUsed,
                    out _);

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

            if (!dataRead || len != charCount)
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


    static void AppendToBuffer(ref byte[] buffer, ReadOnlySpan<byte> segment, ref int length)
    {
        if (length + segment.Length > buffer.Length)
        {
            var newSize = Math.Max(buffer.Length * 2, length + segment.Length);
            var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
            Array.Copy(buffer, newBuffer, length);
            ArrayPool<byte>.Shared.Return(buffer);
            buffer = newBuffer;
        }

        segment.CopyTo(buffer.AsSpan(length));
        length += segment.Length;
    }

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
                if (!char.IsWhiteSpace(c))
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
        scratchBufferUsed = false;
    }
}