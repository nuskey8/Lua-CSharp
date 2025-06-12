using Lua.IO;
using Lua.Runtime;

namespace Lua;

public static class LuaStateExtensions
{
    public static ValueTask<int> DoStringAsync(this LuaState state, string source, Memory<LuaValue> buffer, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        return state.RootAccess.DoStringAsync(source, buffer, chunkName, cancellationToken);
    }

    public static ValueTask<LuaValue[]> DoStringAsync(this LuaState state, string source, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        return state.RootAccess.DoStringAsync(source, chunkName, cancellationToken);
    }

    public static ValueTask<int> DoBytesAsync(this LuaState state, ReadOnlySpan<byte> source, Memory<LuaValue> buffer, string chunkName, CancellationToken cancellationToken = default)
    {
        return state.RootAccess.DoBytesAsync(source, buffer, chunkName, cancellationToken);
    }

    public static ValueTask<LuaValue[]> DoBytesAsync(this LuaState state, ReadOnlySpan<byte> source, string chunkName, CancellationToken cancellationToken = default)
    {
        return state.RootAccess.DoBytesAsync(source, chunkName, cancellationToken);
    }

    public static ValueTask<int> DoFileAsync(this LuaState state, string path, Memory<LuaValue> buffer, CancellationToken cancellationToken = default)
    {
        return state.RootAccess.DoFileAsync(path, buffer, cancellationToken);
    }

    public static ValueTask<LuaValue[]> DoFileAsync(this LuaState state, string path, CancellationToken cancellationToken = default)
    {
        return state.RootAccess.DoFileAsync(path, cancellationToken);
    }

    public static async ValueTask<LuaClosure> LoadFileAsync(this LuaState state, string fileName, string mode, LuaTable? environment, CancellationToken cancellationToken)
    {
        var name = "@" + fileName;
        using var stream = await state.FileSystem.Open(fileName, LuaFileOpenMode.Read, cancellationToken);
        var source = await stream.ReadAllAsync(cancellationToken);
        LuaClosure closure = state.Load(source, name, environment);

        return closure;
    }
}