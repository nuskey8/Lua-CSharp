namespace Lua.IO;

public interface ILuaByteStream
{
    ValueTask<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken);

    ValueTask<int> ReadByteAsync(CancellationToken cancellationToken);
}