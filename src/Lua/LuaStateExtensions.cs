using Lua.CodeAnalysis.Compilation;
using Lua.CodeAnalysis.Syntax;

namespace Lua;

public static class LuaStateExtensions
{
    public static async ValueTask<int> DoStringAsync(this LuaState state, string source, Memory<LuaValue> buffer, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        var syntaxTree = LuaSyntaxTree.Parse(source, chunkName);
        var chunk = LuaCompiler.Default.Compile(syntaxTree, chunkName);
        using var result = await state.RunAsync(chunk, cancellationToken);
        result.AsSpan().CopyTo(buffer.Span);
        return result.Count;
    }

    public static async ValueTask<LuaValue[]> DoStringAsync(this LuaState state, string source, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        var syntaxTree = LuaSyntaxTree.Parse(source, chunkName);
        var chunk = LuaCompiler.Default.Compile(syntaxTree, chunkName);
        using var result = await state.RunAsync(chunk, cancellationToken);
        return result.AsSpan().ToArray();
    }

    public static async ValueTask<int> DoFileAsync(this LuaState state, string path, Memory<LuaValue> buffer, CancellationToken cancellationToken = default)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken);
        var fileName = "@" + Path.GetFileName(path);
        var syntaxTree = LuaSyntaxTree.Parse(text, fileName);
        var chunk = LuaCompiler.Default.Compile(syntaxTree, fileName);
        using var result = await state.RunAsync(chunk, cancellationToken);
        result.AsSpan().CopyTo(buffer.Span);
        return result.Count;
    }

    public static async ValueTask<LuaValue[]> DoFileAsync(this LuaState state, string path, CancellationToken cancellationToken = default)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken);
        var fileName = "@" + Path.GetFileName(path);
        var syntaxTree = LuaSyntaxTree.Parse(text, fileName);
        var chunk = LuaCompiler.Default.Compile(syntaxTree, fileName);
        using var result = await state.RunAsync(chunk, cancellationToken);
        return result.AsSpan().ToArray();
    }
}