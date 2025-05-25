using Lua.Runtime;

namespace Lua;

public static class LuaStateExtensions
{
    public static ValueTask<int> DoStringAsync(this LuaState state, string source, Memory<LuaValue> buffer, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        return state.TopLevelAccess.DoStringAsync(source, buffer, chunkName, cancellationToken);
    }

    public static ValueTask<LuaValue[]> DoStringAsync(this LuaState state, string source, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        return state.TopLevelAccess.DoStringAsync(source, chunkName, cancellationToken);
    }

    public static ValueTask<int> DoFileAsync(this LuaState state, string path, Memory<LuaValue> buffer, CancellationToken cancellationToken = default)
    {
        return state.TopLevelAccess.DoFileAsync(path, buffer, cancellationToken);
    }

    public static ValueTask<LuaValue[]> DoFileAsync(this LuaState state, string path, CancellationToken cancellationToken = default)
    {
        return state.TopLevelAccess.DoFileAsync(path, cancellationToken);
    }

    public static async ValueTask<LuaClosure> LoadFileAsync(this LuaState state, string fileName, string mode, LuaTable? environment, CancellationToken cancellationToken)
    {
        var name = "@" + fileName;
        LuaClosure closure;
        {
            using var file = await state.FileSystem.ReadFileContentAsync(fileName, cancellationToken);
            closure = file.Type == LuaFileContentType.Bytes
                ? state.Load(file.ReadBytes(), name, mode, environment)
                : state.Load(file.ReadText(), name, environment);
        }
        return closure;
    }
}