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
    bool checkBom;
    int pendingCharacter = -1;

    /// <param name="stringMode">
    /// In byte mode, every byte is decoded to one char (0x00-0xFF) and a UTF-8 BOM is not skipped,
    /// like the reference C implementation. Otherwise the stream is decoded as UTF-8.
    /// </param>
    public Utf8Reader(LuaStringMode stringMode = LuaStringMode.Utf16)
    {
        byteStrings = stringMode == LuaStringMode.Bytes;
        encoding = byteStrings ? LuaByteEncoding.Instance : Encoding.UTF8;
        checkBom = !byteStrings;
    }

    public long Remain => bufLen - bufPos;

    bool ReadBuffer(Stream stream)
    {
        bufLen = stream.Read(buffer, 0, buffer.Length);
        bufPos = 0;
        return bufLen != 0;
    }

    void PrepareText(Stream stream)
    {
        checkBom = false;
        if (stream.CanSeek && stream.Position - Remain != 0)
            return;
        if (PeekByte(stream) != 0xEF)
            return;

        var count = bufLen - bufPos;
        if (count < BomUtility.BomUtf8.Length)
        {
            buffer.AsSpan(bufPos, count).CopyTo(buffer);
            bufPos = 0;
            bufLen = count;
            while (bufLen < BomUtility.BomUtf8.Length)
            {
                var read = stream.Read(buffer, bufLen, BomUtility.BomUtf8.Length - bufLen);
                if (read == 0)
                    break;
                bufLen += read;
            }
        }
        if (buffer.AsSpan(bufPos, bufLen - bufPos).StartsWith(BomUtility.BomUtf8))
            bufPos += BomUtility.BomUtf8.Length;
    }

    string DecodeText(ReadOnlySpan<byte> bytes)
    {
        var text = encoding.GetString(bytes);
        return pendingCharacter < 0 ? text : PrependPendingCharacter(text);
    }

    string PrependPendingCharacter(string text)
    {
        var first = (char)pendingCharacter;
        pendingCharacter = -1;
        return string.Create(
            text.Length + 1,
            (first, text),
            static (span, state) =>
            {
                span[0] = state.first;
                state.text.AsSpan().CopyTo(span[1..]);
            }
        );
    }

    public string? ReadLine(Stream stream, bool keepEol = false)
    {
        if (checkBom)
            PrepareText(stream);
        if (pendingCharacter is '\r' or '\n')
        {
            var first = (char)pendingCharacter;
            pendingCharacter = -1;
            var crlf = first == '\r' && PeekByte(stream) == '\n';
            if (crlf)
                bufPos++;
            return !keepEol ? ""
                : crlf ? "\r\n"
                : first.ToString();
        }
        using PooledList<byte> result = new();
        while (true)
        {
            if (bufPos >= bufLen && !ReadBuffer(stream))
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
                    return DecodeText(result.AsSpan());
                }
                if (buffer[bufPos] == '\n')
                {
                    bufPos++;
                    if (keepEol)
                        count++;
                }
            }
            if (result.Count == 0)
                return DecodeText(span[..count]);
            result.AddRange(span[..count]);
            return DecodeText(result.AsSpan());
        }
        return result.Count == 0 && pendingCharacter < 0 ? null : DecodeText(result.AsSpan());
    }

    public string ReadToEnd(Stream stream)
    {
        if (checkBom)
            PrepareText(stream);
        using PooledList<byte> result = new();
        while (true)
        {
            if (bufPos >= bufLen && !ReadBuffer(stream))
                break;
            result.AddRange(buffer.AsSpan(bufPos, bufLen - bufPos));
            bufPos = bufLen;
        }
        return DecodeText(result.AsSpan());
    }

    public int ReadByte(Stream stream)
    {
        var value = PeekByte(stream);
        if (value >= 0)
        {
            checkBom = false;
            bufPos++;
        }
        return value;
    }

    public void CopyBufferedBytes(IBufferWriter<byte> writer)
    {
        var count = bufLen - bufPos;
        if (count == 0)
            return;
        checkBom = false;
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
        var resultBuffer = ArrayPool<char>.Shared.Rent(charCount);

        try
        {
            if (byteStrings)
            {
                while (len < charCount)
                {
                    if (bufPos >= bufLen && !ReadBuffer(stream))
                        break;
                    var count = Math.Min(bufLen - bufPos, charCount - len);
                    LuaByteEncoding.Instance.GetChars(
                        buffer.AsSpan(bufPos, count),
                        resultBuffer.AsSpan(len, count)
                    );
                    len += count;
                    bufPos += count;
                }
            }
            else
                len = ReadText(stream, resultBuffer.AsSpan(0, charCount));

            return len == 0 ? null : resultBuffer.AsSpan(0, len).ToString();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(resultBuffer);
        }
    }

    int ReadText(Stream stream, Span<char> destination)
    {
        if (checkBom)
            PrepareText(stream);
        var len = 0;
        if (pendingCharacter >= 0)
        {
            destination[len++] = (char)pendingCharacter;
            pendingCharacter = -1;
        }
        Span<char> pair = stackalloc char[2];
        while (len < destination.Length)
        {
            var eof = bufPos >= bufLen && !ReadBuffer(stream);
            if (eof && decoder is null)
                break;

            ReadOnlySpan<byte> byteSpan = new(buffer, bufPos, bufLen - bufPos);
            scoped Span<char> charSpan = destination[len..];
            // A UTF-8 scalar can require two UTF-16 code units. Limit final-slot
            // input to one sequence, retaining any second code unit.
            var singleSlot = charSpan.Length == 1;
            if (singleSlot)
            {
                if (!byteSpan.IsEmpty)
                {
                    var sequenceLength = byteSpan[0] switch
                    {
                        >= 0xF0 and <= 0xF4 => 4,
                        >= 0xE0 and <= 0xEF => 3,
                        >= 0xC2 and <= 0xDF => 2,
                        _ => 1,
                    };
                    byteSpan = byteSpan[..Math.Min(byteSpan.Length, sequenceLength)];
                }
                charSpan = pair;
            }
            decoder ??= encoding.GetDecoder();
            decoder.Convert(
                byteSpan,
                charSpan,
                eof,
                out var bytesUsed,
                out var charsUsed,
                out var completed
            );

            if (charsUsed > 0)
            {
                if (singleSlot)
                {
                    destination[len++] = pair[0];
                    if (charsUsed == 2)
                        pendingCharacter = pair[1];
                }
                else
                    len += charsUsed;
            }

            bufPos += bytesUsed;
            if (eof && completed)
                break;
        }
        return len;
    }

    static bool IsAsciiSpace(char c) => c == ' ' || (c >= '\t' && c <= '\r');

    public void Clear(bool atStart = false, bool keepPendingCharacter = false)
    {
        bufPos = 0;
        bufLen = 0;
        decoder?.Reset();
        if (!keepPendingCharacter)
            pendingCharacter = -1;
        checkBom = atStart && !byteStrings;
    }

    public double? ReadNumber(Stream stream)
    {
        var resultBuffer = ArrayPool<char>.Shared.Rent(64); // Numbers shouldn't be too long
        var len = 0;
        NumberReaderHelper.Scanner scanner = new();

        try
        {
            checkBom = false;
            if (pendingCharacter >= 0)
            {
                var c = (char)pendingCharacter;
                if (!char.IsWhiteSpace(c))
                {
                    if (!scanner.TryConsume(c))
                        return null;
                    resultBuffer[len++] = c;
                }
                pendingCharacter = -1;
            }
            // Skip leading whitespace
            while (len == 0)
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

                bufPos++;
            }

            while (true)
            {
                var b = PeekByte(stream);
                if (b < 0 || !scanner.TryConsume((char)b))
                    break;
                if (len == resultBuffer.Length)
                {
                    var expanded = ArrayPool<char>.Shared.Rent(resultBuffer.Length * 2);
                    resultBuffer.AsSpan(0, len).CopyTo(expanded);
                    ArrayPool<char>.Shared.Return(resultBuffer);
                    resultBuffer = expanded;
                }
                resultBuffer[len++] = (char)b;
                bufPos++;
            }
            return len == 0 ? null : NumberReaderHelper.ParseNumber(resultBuffer.AsSpan(0, len));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(resultBuffer);
        }
    }

    public bool IsEndOfStream(Stream stream)
    {
        return pendingCharacter < 0 && PeekByte(stream) < 0;
    }

    int PeekByte(Stream stream)
    {
        if (bufPos >= bufLen && !ReadBuffer(stream))
            return -1;
        return buffer[bufPos];
    }

    public void Dispose()
    {
        var rented = buffer;
        buffer = [];
        bufPos = bufLen = 0;
        ArrayPool<byte>.Shared.Return(rented);
    }
}
