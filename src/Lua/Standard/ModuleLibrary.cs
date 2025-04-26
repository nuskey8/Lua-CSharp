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

    public async ValueTask<int> Require(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<string>(0);
        var loaded = context.State.LoadedModules;

        if (!loaded.TryGetValue(arg0, out var loadedTable))
        {
            var module = await context.State.ModuleLoader.LoadAsync(arg0, cancellationToken);
            var chunk = LuaCompiler.Default.Compile(module.ReadText(), module.Name);
            await new LuaClosure(context.State, chunk).InvokeAsync(context, cancellationToken);

            loadedTable = context.Thread.Stack.Get(context.ReturnFrameBase);
            loaded[arg0] = loadedTable;
        }

        return context.Return(loadedTable);
    }
}