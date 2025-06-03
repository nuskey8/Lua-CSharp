using Lua.IO;
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

    public static ValueTask<int> DoBytesAsync(this LuaState state, ReadOnlySpan<byte> source, Memory<LuaValue> buffer, string chunkName, CancellationToken cancellationToken = default)
    {
        return state.TopLevelAccess.DoBytesAsync(source, buffer, chunkName, cancellationToken);
    }

    public static ValueTask<LuaValue[]> DoBytesAsync(this LuaState state, ReadOnlySpan<byte> source, string chunkName, CancellationToken cancellationToken = default)
    {
        return state.TopLevelAccess.DoBytesAsync(source, chunkName, cancellationToken);
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
        
        var openFlags = LuaFileMode.Read;
        if (mode.Contains('b'))
        {
            openFlags |= LuaFileMode.Binary;
        }
        if (mode.Contains('t'))
        {
            openFlags |= LuaFileMode.Text;
        }

        using var stream = state.FileSystem.Open(fileName, openFlags);
        using var content = await stream.ReadToEndAsync(cancellationToken);
            
        if (content.Type == LuaFileContentType.Bytes)
        {
            closure = state.Load(content.ReadBytes().Span, name, mode, environment);
        }
        else
        {
            closure = state.Load(content.ReadText().Span, name, environment);
        }

        return closure;
    }
}