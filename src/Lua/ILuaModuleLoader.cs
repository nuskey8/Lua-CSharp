namespace Lua;

public interface ILuaModuleLoader
{
    bool Exists(string moduleName);
    ValueTask<LuaModule> LoadAsync(
        string moduleName,
        CancellationToken cancellationToken = default
    );
}
