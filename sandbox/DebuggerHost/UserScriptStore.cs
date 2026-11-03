using System.Collections.Concurrent;
using Lua;
using Lua.Debugging;

sealed class UserScriptStore(string directory) : ILuaModuleLoader
{
    const string Prefix = "=user/";
    readonly ConcurrentDictionary<string, string> contents = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
    );

    public string? ResolvePath(string name) =>
        name.StartsWith(Prefix, StringComparison.Ordinal)
            ? Path.GetFullPath(Path.Combine(directory, name[Prefix.Length..]))
            : null;

    public ValueTask<string?> ReadSourceAsync(LuaDebugSource source, CancellationToken _) =>
        new(source.Path != null && contents.TryGetValue(source.Path, out var text) ? text : null);

    public bool Exists(string moduleName) => FindModule(moduleName) != null;

    public ValueTask<LuaModule> LoadAsync(
        string moduleName,
        CancellationToken cancellationToken = default
    ) =>
        ReadAsync(
            FindModule(moduleName)
                ?? throw new FileNotFoundException("Module not found: " + moduleName),
            cancellationToken
        );

    public async ValueTask<LuaModule> ReadAsync(
        string relativePath,
        CancellationToken cancellationToken
    )
    {
        var path = Path.GetFullPath(Path.Combine(directory, relativePath));
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        contents[path] = content;
        return new LuaModule(
            Prefix + Path.GetRelativePath(directory, path).Replace('\\', '/'),
            content
        );
    }

    string? FindModule(string name)
    {
        var relative = name.Replace('.', Path.DirectorySeparatorChar);
        if (File.Exists(Path.Combine(directory, relative + ".lua")))
            return relative + ".lua";
        var init = Path.Combine(relative, "init.lua");
        return File.Exists(Path.Combine(directory, init)) ? init : null;
    }
}
