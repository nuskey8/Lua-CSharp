namespace Lua.IO;

/// <summary>
/// Host file operations. Paths and temporary filenames are .NET text in every string mode.
/// Stream contents use the Lua representation configured for the state.
/// </summary>
public interface ILuaFileSystem
{
    bool IsReadable(string path);
    ValueTask<ILuaStream> Open(
        string path,
        LuaFileOpenMode mode,
        CancellationToken cancellationToken
    );
    ValueTask Rename(string oldName, string newName, CancellationToken cancellationToken);
    ValueTask Remove(string path, CancellationToken cancellationToken);

    string DirectorySeparator => "/";

    string GetTempFileName();
    ValueTask<ILuaStream> OpenTempFileStream(CancellationToken cancellationToken);
}
