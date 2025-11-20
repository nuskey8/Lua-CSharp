namespace Lua.IO;

public interface ILuaFileLoader
{
    bool Exists(string path);
    ValueTask<ILuaStream> LoadAsync(string path, CancellationToken cancellationToken = default);
}