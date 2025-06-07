using System.Buffers;
using System.Text;

namespace Lua.Internal;

internal sealed class Utf8Reader
{
    [ThreadStatic]
    static byte[]? scratchBuffer;

    [ThreadStatic]
    internal static bool scratchBufferUsed;

    private readonly byte[] buffer;
    private int bufPos, bufLen;
    private Decoder? decoder;

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

    public string? ReadLine(Stream stream)
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
                    bufPos = 0;
                    if (bufLen == 0)
                        break; // EOF
                }

                var span = new Span<byte>(buffer, bufPos, bufLen - bufPos);
                int idx = span.IndexOfAny((byte)'\r', (byte)'\n');

                if (idx >= 0)
                {
                    AppendToBuffer(ref resultBuffer, span[..idx], ref lineLen);

                    byte nl = span[idx];
                    bufPos += idx + 1;

                    // CRLF
                    if (nl == (byte)'\r' && bufPos < bufLen && buffer[bufPos] == (byte)'\n')
                        bufPos++;

                    // 行を返す
                    return Encoding.UTF8.GetString(resultBuffer, 0, lineLen);
                }
                else
                {
                    // 改行なし → 全部行バッファへ
                    AppendToBuffer(ref resultBuffer, span, ref lineLen);
                    bufPos = bufLen;
                }
            }

            if (lineLen == 0)
                return null;
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

                    bufPos = 0;
                    if (bufLen == 0)
                        break; // EOF
                }

                var span = new Span<byte>(buffer, bufPos, bufLen - bufPos);
                AppendToBuffer(ref resultBuffer, span, ref len);
                bufPos = bufLen;
            }

            if (len == 0)
                return "";
            return Encoding.UTF8.GetString(resultBuffer, 0, len);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(resultBuffer);
        }
    }

    public string? Read(Stream stream, int charCount)
    {
        if (charCount < 0) throw new ArgumentOutOfRangeException(nameof(charCount));
        if (charCount == 0) return string.Empty;

        var len = 0;
        bool dataRead = false;
        var resultBuffer = ArrayPool<char>.Shared.Rent(charCount);

        try
        {
            while (len < charCount)
            {
                if (bufPos >= bufLen)
                {
                    bufLen = stream.Read(buffer, 0, buffer.Length);
                    bufPos = 0;
                    if (bufLen == 0) break; // EOF
                }

                var byteSpan = new ReadOnlySpan<byte>(buffer, bufPos, bufLen - bufPos);
                var charSpan = new Span<char>(resultBuffer, len, charCount - len);
                decoder ??= Encoding.UTF8.GetDecoder();
                decoder.Convert(
                    byteSpan,
                    charSpan,
                    flush: false,
                    out int bytesUsed,
                    out int charsUsed,
                    out _);

                if (charsUsed > 0)
                {
                    len += charsUsed;
                    dataRead = true;
                }

                bufPos += bytesUsed;
                if (bytesUsed == 0) break;
            }

            if (!dataRead || len != charCount) return null;
            return resultBuffer.AsSpan(0, len).ToString();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(resultBuffer);
        }
    }


    private static void AppendToBuffer(ref byte[] buffer, ReadOnlySpan<byte> segment, ref int length)
    {
        if (length + segment.Length > buffer.Length)
        {
            int newSize = Math.Max(buffer.Length * 2, length + segment.Length);
            var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
            Array.Copy(buffer, newBuffer, length);
            ArrayPool<byte>.Shared.Return(buffer);
        }

        segment.CopyTo(buffer.AsSpan(length));
        length += segment.Length;
    }

    public void Clear()
    {
        bufPos = 0;
        bufLen = 0;
    }

    public void Dispose()
    {
        scratchBufferUsed = false;
    }
}