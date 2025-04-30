namespace Lua.Loaders;

public sealed class FileModuleLoader : ILuaModuleLoader
{
    public static readonly FileModuleLoader Instance = new();

    public bool Exists(string moduleName)
    {
        return File.Exists(moduleName);
    }

    public async ValueTask<LuaModule> LoadAsync(string moduleName, CancellationToken cancellationToken = default)
    {
        var path = moduleName;
        if (!Path.HasExtension(path)) path += ".lua";
        var text = await File.ReadAllBytesAsync(path, cancellationToken);
        return new LuaModule(moduleName, text);
    }
}