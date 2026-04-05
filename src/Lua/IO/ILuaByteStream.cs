using System.Buffers;

namespace Lua.IO;

public interface ILuaByteStream
{
    ValueTask<int> ReadByteAsync(CancellationToken cancellationToken);

    ValueTask ReadBytesAsync(IBufferWriter<byte> writer, CancellationToken cancellationToken);
}