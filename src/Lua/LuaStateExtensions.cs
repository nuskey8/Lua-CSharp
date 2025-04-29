using Lua.CodeAnalysis.Compilation;
using Lua.CodeAnalysis;

namespace Lua;

public static class LuaStateExtensions
{
    public static async ValueTask<int> DoStringAsync(this LuaState state, string source, Memory<LuaValue> buffer, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        var closure = state.Compile(source, chunkName ?? source);
        using var result = await state.RunAsync(closure, cancellationToken);
        result.AsSpan()[..Math.Min(buffer.Length, result.Length)].CopyTo(buffer.Span);
        return result.Count;
    }

    public static async ValueTask<LuaValue[]> DoStringAsync(this LuaState state, string source, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        var chunk = state.Compile(source, chunkName ?? source);
        using var result = await state.RunAsync(chunk, cancellationToken);
        return result.AsSpan().ToArray();
    }

    public static async ValueTask<int> DoFileAsync(this LuaState state, string path, Memory<LuaValue> buffer, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var fileName = "@" + Path.GetFileName(path);
        var closure = state.Compile(bytes, fileName);
        using var result = await state.RunAsync(closure, cancellationToken);
        result.AsSpan()[..Math.Min(buffer.Length, result.Length)].CopyTo(buffer.Span);
        return result.Count;
    }

    public static async ValueTask<LuaValue[]> DoFileAsync(this LuaState state, string path, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var fileName = "@" + Path.GetFileName(path);
        var closure = state.Compile(bytes, fileName);
        using var result = await state.RunAsync(closure, cancellationToken);
        return result.AsSpan().ToArray();
    }
}