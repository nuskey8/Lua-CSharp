using System.Buffers;
using System.Runtime.CompilerServices;
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

    bool ReadBuffer(Stream stream, CancellationToken cancellationToken, int keep = 0)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (keep > 0)
            buffer.AsSpan(bufPos, keep).CopyTo(buffer);
        var read = stream.Read(buffer, keep, buffer.Length - keep);
        bufLen = keep + read;
        bufPos = 0;
        return read != 0;
    }

    void PrepareText(Stream stream, CancellationToken cancellationToken)
    {
        checkBom = false;
        if (stream.CanSeek && stream.Position - Remain != 0)
            return;
        if (PeekByte(stream, cancellationToken) != 0xEF)
            return;

        var count = bufLen - bufPos;
        if (count < BomUtility.BomUtf8.Length)
        {
            buffer.AsSpan(bufPos, count).CopyTo(buffer);
            bufPos = 0;
            bufLen = count;
            while (bufLen < BomUtility.BomUtf8.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
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

    public string? ReadLine(Stream stream, bool keepEol, CancellationToken cancellationToken)
    {
        if (checkBom)
            PrepareText(stream, cancellationToken);
        if (pendingCharacter is '\r' or '\n')
        {
            var first = (char)pendingCharacter;
            pendingCharacter = -1;
            var crlf = first == '\r' && PeekByte(stream, cancellationToken) == '\n';
            if (crlf)
                bufPos++;
            return !keepEol ? ""
                : crlf ? "\r\n"
                : first.ToString();
        }
        using PooledList<byte> result = new();
        while (true)
        {
            if (bufPos >= bufLen && !ReadBuffer(stream, cancellationToken))
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
                    if (PeekByte(stream, cancellationToken) == '\n')
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

    public string ReadToEnd(Stream stream, CancellationToken cancellationToken)
    {
        if (checkBom)
            PrepareText(stream, cancellationToken);
        using PooledList<byte> result = new();
        while (true)
        {
            if (bufPos >= bufLen && !ReadBuffer(stream, cancellationToken))
                break;
            result.AddRange(buffer.AsSpan(bufPos, bufLen - bufPos));
            bufPos = bufLen;
        }
        return DecodeText(result.AsSpan());
    }

    public int ReadByte(Stream stream, CancellationToken cancellationToken)
    {
        var value = PeekByte(stream, cancellationToken);
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

    public string? Read(Stream stream, int charCount, CancellationToken cancellationToken)
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
                    if (bufPos >= bufLen && !ReadBuffer(stream, cancellationToken))
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
                len = ReadText(stream, resultBuffer.AsSpan(0, charCount), cancellationToken);

            return len == 0 ? null : resultBuffer.AsSpan(0, len).ToString();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(resultBuffer);
        }
    }

    int ReadText(Stream stream, Span<char> destination, CancellationToken cancellationToken)
    {
        if (checkBom)
            PrepareText(stream, cancellationToken);
        var len = 0;
        if (pendingCharacter >= 0)
        {
            destination[len++] = (char)pendingCharacter;
            pendingCharacter = -1;
        }
        Span<char> pair = stackalloc char[2];
        while (len < destination.Length)
        {
            if (bufPos >= bufLen && !ReadBuffer(stream, cancellationToken))
                break;

            ReadOnlySpan<byte> byteSpan = new(buffer, bufPos, bufLen - bufPos);
            scoped Span<char> charSpan = destination[len..];
            // A UTF-8 scalar can require two UTF-16 code units. Limit final-slot
            // input to one sequence, retaining any second code unit.
            var singleSlot = charSpan.Length == 1;
            if (singleSlot)
            {
                var first = byteSpan[0];
                if (first < 0x80)
                {
                    destination[len++] = (char)first;
                    bufPos++;
                    continue;
                }
                var sequenceLength = Utf8SequenceLength(first);
                byteSpan = byteSpan[..Math.Min(byteSpan.Length, sequenceLength)];
                charSpan = pair;
            }
            // Keep incomplete sequences in the byte buffer, where other formats
            // and seeks can account for them without depending on decoder state.
            var completeLength = CompleteUtf8Length(byteSpan);
            if (completeLength == 0)
            {
                if (ReadBuffer(stream, cancellationToken, bufLen - bufPos))
                    continue;
                byteSpan = buffer.AsSpan(bufPos, bufLen - bufPos);
            }
            else
                byteSpan = byteSpan[..completeLength];
            decoder ??= encoding.GetDecoder();
            decoder.Convert(
                byteSpan,
                charSpan,
                flush: true,
                out var bytesUsed,
                out var charsUsed,
                out _
            );

            if (singleSlot)
            {
                destination[len++] = pair[0];
                if (charsUsed == 2)
                    pendingCharacter = pair[1];
            }
            else
                len += charsUsed;

            bufPos += bytesUsed;
        }
        return len;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Utf8SequenceLength(byte first) =>
        first switch
        {
            >= 0xF0 and <= 0xF4 => 4,
            >= 0xE0 and <= 0xEF => 3,
            >= 0xC2 and <= 0xDF => 2,
            _ => 1,
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int CompleteUtf8Length(ReadOnlySpan<byte> bytes)
    {
        var start = bytes.Length - 1;
        if (bytes[start] < 0x80)
            return bytes.Length;
        while (start > 0 && bytes.Length - start < 4 && (bytes[start] & 0xC0) == 0x80)
            start--;
        return Utf8SequenceLength(bytes[start]) > bytes.Length - start ? start : bytes.Length;
    }

    static bool IsAsciiSpace(char c) => c == ' ' || (c >= '\t' && c <= '\r');

    public void Clear(bool atStart = false, bool keepPendingCharacter = false)
    {
        bufPos = 0;
        bufLen = 0;
        if (!keepPendingCharacter)
            pendingCharacter = -1;
        checkBom = atStart && !byteStrings;
    }

    public double? ReadNumber(Stream stream, CancellationToken cancellationToken)
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
                var b = PeekByte(stream, cancellationToken);
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
                var b = PeekByte(stream, cancellationToken);
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

    public bool IsEndOfStream(Stream stream, CancellationToken cancellationToken)
    {
        return pendingCharacter < 0 && PeekByte(stream, cancellationToken) < 0;
    }

    int PeekByte(Stream stream, CancellationToken cancellationToken)
    {
        if (bufPos >= bufLen && !ReadBuffer(stream, cancellationToken))
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
