namespace Lua.IO;

public interface ILuaFileSystem
{
    public bool IsReadable(string path);
    public ILuaStream Open(string path, LuaFileMode mode);
    public void Rename(string oldName, string newName);
    public void Remove(string path);
    public string DirectorySeparator { get; }
    public string GetTempFileName();
    public ILuaStream OpenTempFileStream();
}