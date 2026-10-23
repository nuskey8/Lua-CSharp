using System.Runtime.InteropServices;

namespace Lua.Internal;

// Encodes UTF-16 text as UTF-8, storing each output byte in one char.
// The destination must have at least Encoding.UTF8.GetByteCount(source) chars.
static class Utf8CharEncoder
{
    public static int Encode(ReadOnlySpan<char> source, Span<char> destination)
    {
        var written = 0;
        for (var i = 0; i < source.Length; i++)
        {
            uint value = source[i];
            if (value <= 0x7F)
            {
                // Copy four ASCII code units together, including within mixed text.
                if (source.Length - i >= 4)
                {
                    var block = MemoryMarshal.Read<ulong>(
                        MemoryMarshal.AsBytes(source.Slice(i, 4))
                    );
                    if ((block & 0xFF80FF80FF80FF80UL) == 0)
                    {
                        BitConverter.TryWriteBytes(
                            MemoryMarshal.AsBytes(destination.Slice(written, 4)),
                            block
                        );
                        written += 4;
                        i += 3;
                        continue;
                    }
                }
                destination[written++] = (char)value;
            }
            else if (value <= 0x7FF)
            {
                destination[written++] = (char)(0xC0 | (value >> 6));
                destination[written++] = (char)(0x80 | (value & 0x3F));
            }
            else
            {
                if (char.IsSurrogate((char)value))
                {
                    if (
                        char.IsHighSurrogate((char)value)
                        && i + 1 < source.Length
                        && char.IsLowSurrogate(source[i + 1])
                    )
                    {
                        value = (uint)char.ConvertToUtf32(source[i], source[i + 1]);
                        i++;
                    }
                    else
                    {
                        // Match Encoding.UTF8's replacement of each unpaired surrogate.
                        value = 0xFFFD;
                    }
                }

                if (value > 0xFFFF)
                {
                    destination[written++] = (char)(0xF0 | (value >> 18));
                    destination[written++] = (char)(0x80 | ((value >> 12) & 0x3F));
                }
                else
                {
                    destination[written++] = (char)(0xE0 | (value >> 12));
                }
                destination[written++] = (char)(0x80 | ((value >> 6) & 0x3F));
                destination[written++] = (char)(0x80 | (value & 0x3F));
            }
        }
        return written;
    }
}
