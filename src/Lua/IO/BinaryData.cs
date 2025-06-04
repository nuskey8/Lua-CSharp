using System.Buffers;

namespace Lua.IO;

public class BinaryData(ReadOnlyMemory<byte> bytes) : IBinaryData
{
    public ReadOnlyMemory<byte> Memory => bytes;
}

public interface IBinaryData
{
    /// <summary>
    /// Gets the bytes of the binary data.
    /// </summary>
    public ReadOnlyMemory<byte> Memory { get; }
}