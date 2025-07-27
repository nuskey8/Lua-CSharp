using Lua.IO;
using Lua.Runtime;

namespace Lua;

public static class LuaStateExtensions
{
    public static ValueTask<int> DoStringAsync(this LuaGlobalState globalState, string source, Memory<LuaValue> buffer, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        return globalState.RootAccess.DoStringAsync(source, buffer, chunkName, cancellationToken);
    }

    public static ValueTask<LuaValue[]> DoStringAsync(this LuaGlobalState globalState, string source, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        return globalState.RootAccess.DoStringAsync(source, chunkName, cancellationToken);
    }

    public static ValueTask<int> ExecuteAsync(this LuaGlobalState globalState, ReadOnlySpan<byte> source, Memory<LuaValue> buffer, string chunkName, CancellationToken cancellationToken = default)
    {
        return globalState.RootAccess.ExecuteAsync(source, buffer, chunkName, cancellationToken);
    }

    public static ValueTask<LuaValue[]> ExecuteAsync(this LuaGlobalState globalState, ReadOnlySpan<byte> source, string chunkName, CancellationToken cancellationToken = default)
    {
        return globalState.RootAccess.ExecuteAsync(source, chunkName, cancellationToken);
    }

    public static ValueTask<int> DoFileAsync(this LuaGlobalState globalState, string path, Memory<LuaValue> buffer, CancellationToken cancellationToken = default)
    {
        return globalState.RootAccess.DoFileAsync(path, buffer, cancellationToken);
    }

    public static ValueTask<LuaValue[]> DoFileAsync(this LuaGlobalState globalState, string path, CancellationToken cancellationToken = default)
    {
        return globalState.RootAccess.DoFileAsync(path, cancellationToken);
    }

    public static async ValueTask<LuaClosure> LoadFileAsync(this LuaGlobalState globalState, string fileName, string mode, LuaTable? environment, CancellationToken cancellationToken)
    {
        var name = "@" + fileName;
        using var stream = await globalState.FileSystem.Open(fileName, LuaFileOpenMode.Read, cancellationToken);
        var source = await stream.ReadAllAsync(cancellationToken);
        var closure = globalState.Load(source, name, environment);

        return closure;
    }
}