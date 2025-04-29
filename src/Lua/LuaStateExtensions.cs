namespace Lua;

public static class LuaStateExtensions
{
    public static ValueTask<int> DoStringAsync(this LuaState state, string source, Memory<LuaValue> buffer, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        return state.MainThread.DoStringAsync(source, buffer, chunkName, cancellationToken);
    }

    public static ValueTask<LuaValue[]> DoStringAsync(this LuaState state, string source, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        return state.MainThread.DoStringAsync(source, chunkName, cancellationToken);
    }

    public static ValueTask<int> DoFileAsync(this LuaState state, string path, Memory<LuaValue> buffer, CancellationToken cancellationToken = default)
    {
        return state.MainThread.DoFileAsync(path, buffer, cancellationToken);
    }

    public static ValueTask<LuaValue[]> DoFileAsync(this LuaState state, string path, CancellationToken cancellationToken = default)
    {
        return state.MainThread.DoFileAsync(path, cancellationToken);
    }
}