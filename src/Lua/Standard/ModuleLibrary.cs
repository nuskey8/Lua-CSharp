using Lua.CodeAnalysis.Compilation;
using Lua.Internal;
using Lua.Runtime;

namespace Lua.Standard;

public sealed class ModuleLibrary
{
    public static readonly ModuleLibrary Instance = new();

    public ModuleLibrary()
    {
        RequireFunction = new("require", Require);
    }

    public readonly LuaFunction RequireFunction;

    public async ValueTask<int> Require(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<string>(0);
        var loaded = context.State.LoadedModules;

        if (!loaded.TryGetValue(arg0, out var loadedTable))
        {
            var module = await context.State.ModuleLoader.LoadAsync(arg0, cancellationToken);
            var chunk = LuaCompiler.Default.Compile(module.ReadText(), module.Name);

            using var methodBuffer = new PooledArray<LuaValue>(1);
            await new LuaClosure(context.State, chunk).InvokeAsync(context, methodBuffer.AsMemory(), cancellationToken);

            loadedTable = methodBuffer[0];
            loaded[arg0] = loadedTable;
        }

        buffer.Span[0] = loadedTable;
        return 1;
    }
}