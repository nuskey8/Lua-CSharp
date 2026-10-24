namespace Lua;

public interface ILuaModuleLoader
{
    /// <summary>Checks a module name expressed as normal .NET text, including in byte-string mode.</summary>
    bool Exists(string moduleName);

    /// <summary>
    /// Loads a host module. Names and text source use .NET text; byte source is kept raw.
    /// </summary>
    ValueTask<LuaModule> LoadAsync(
        string moduleName,
        CancellationToken cancellationToken = default
    );
}
