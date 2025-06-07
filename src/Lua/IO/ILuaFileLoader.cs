namespace Lua.IO;

public interface ILuaFileLoader
{
    public bool Exists(string path);
    public ValueTask<LuaFileContent> LoadAsync(string path, CancellationToken cancellationToken = default);
}