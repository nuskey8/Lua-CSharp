namespace Lua.IO;

/// <summary>Loads files by .NET path; stream contents use the state's Lua string representation.</summary>
public interface ILuaFileLoader
{
    bool Exists(string path);
    ValueTask<ILuaStream> LoadAsync(string path, CancellationToken cancellationToken = default);
}
