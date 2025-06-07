namespace Lua.IO;

public interface ILuaFileSystem
{
    public bool IsReadable(string path);
    public ValueTask<ILuaStream> Open(string path, LuaFileMode mode, CancellationToken cancellationToken);
    public ValueTask Rename(string oldName, string newName, CancellationToken cancellationToken);
    public ValueTask Remove(string path, CancellationToken cancellationToken);
    public string DirectorySeparator => "/";
    public string GetTempFileName();
    public ValueTask<ILuaStream> OpenTempFileStream(CancellationToken cancellationToken);
}