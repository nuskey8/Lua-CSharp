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
            LuaClosure closure;
            {
                using var module = await context.State.ModuleLoader.LoadAsync(arg0, cancellationToken);
                closure = module.Type == LuaModuleType.Bytes
                    ? context.State.Load(module.ReadBytes(), module.Name)
                    : context.State.Load(module.ReadText(), module.Name);
            }
            await context.Access.RunAsync(closure, 0, context.ReturnFrameBase, cancellationToken);
            loadedTable = context.Thread.Stack.Get(context.ReturnFrameBase);
            loaded[arg0] = loadedTable;
        }

        return context.Return(loadedTable);
    }
}