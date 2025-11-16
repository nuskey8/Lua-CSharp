namespace Lua.Standard;

public sealed class ModuleLibrary
{
    public static readonly ModuleLibrary Instance = new();
    internal const string LoadedKeyForRegistry = "_LOADED";
    internal const string PreloadKeyForRegistry = "_PRELOAD";

    public ModuleLibrary()
    {
        RequireFunction = new("require", Require);
        SearchPathFunction = new("package.searchpath", SearchPath);
    }

    public readonly LuaFunction RequireFunction;
    public readonly LuaFunction SearchPathFunction;

    public async ValueTask<int> Require(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<string>(0);
        var loaded = context.GlobalState.LoadedModules;

        if (!loaded.TryGetValue(arg0, out var loadedTable))
        {
            LuaFunction loader;
            var moduleLoader = context.GlobalState.ModuleLoader;
            if (moduleLoader != null && moduleLoader.Exists(arg0))
            {
                var module = await moduleLoader.LoadAsync(arg0, cancellationToken);
                loader = module.Type == LuaModuleType.Bytes
                    ? context.State.Load(module.ReadBytes(), module.Name)
                    : context.State.Load(module.ReadText(), module.Name);
            }
            else
            {
                loader = await FindLoader(context.State, arg0, cancellationToken);
            }

            await context.State.RunAsync(loader, 0, context.ReturnFrameBase, cancellationToken);
            loadedTable = context.State.Stack.Get(context.ReturnFrameBase);
            loaded[arg0] = loadedTable;
        }

        return context.Return(loadedTable);
    }

    internal static async ValueTask<string?> FindFile(LuaState state, string name, string pName, string dirSeparator)
    {
        var globalState = state.GlobalState;
        var package = globalState.Environment["package"];
        var p = await state.GetTableAsync(package, pName);
        if (!p.TryReadString(out var path))
        {
            throw new LuaRuntimeException(state, $"package.{pName} must be a string");
        }

        return SearchPath(state, name, path, ".", dirSeparator);
    }

    public ValueTask<int> SearchPath(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var name = context.GetArgument<string>(0);
        var path = context.GetArgument<string>(1);
        var separator = context.GetArgument<string>(2);
        var dirSeparator = context.GetArgument<string>(3);
        var fileName = SearchPath(context.State, name, path, separator, dirSeparator);
        return new(context.Return(fileName ?? LuaValue.Nil));
    }

    internal static string? SearchPath(LuaState state, string name, string path, string separator, string dirSeparator)
    {
        if (separator != "")
        {
            name = name.Replace(separator, dirSeparator);
        }

        var pathSpan = path.AsSpan();
        var nextIndex = pathSpan.IndexOf(';');
        if (nextIndex == -1)
        {
            nextIndex = pathSpan.Length;
        }

        do
        {
            path = pathSpan[..nextIndex].ToString();
            var fileName = path.Replace("?", name);
            if (state.GlobalState.Platform.FileSystem.IsReadable(fileName))
            {
                return fileName;
            }

            if (pathSpan.Length <= nextIndex)
            {
                break;
            }

            pathSpan = pathSpan[(nextIndex + 1)..];
            nextIndex = pathSpan.IndexOf(';');
            if (nextIndex == -1)
            {
                nextIndex = pathSpan.Length;
            }
        } while (nextIndex != -1);

        return null;
    }

    internal static async ValueTask<LuaFunction> FindLoader(LuaState state, string name, CancellationToken cancellationToken)
    {
        var package = state.GlobalState.Environment["package"].Read<LuaTable>();
        var searchers = package["searchers"].Read<LuaTable>();
        for (var i = 0; i < searchers.GetArraySpan().Length; i++)
        {
            var searcher = searchers.GetArraySpan()[i];
            if (searcher.Type == LuaValueType.Nil)
            {
                continue;
            }

            var loader = searcher;
            var top = state.Stack.Count;
            state.Stack.Push(loader);
            state.Stack.Push(name);
            var resultCount = await state.CallAsync(top, top, cancellationToken);
            if (0 < resultCount)
            {
                var result = state.Stack.Get(top);
                if (result.Type == LuaValueType.Function)
                {
                    state.Stack.SetTop(top);
                    return result.Read<LuaFunction>();
                }
            }

            state.Stack.SetTop(top);
        }

        throw new LuaRuntimeException(state, $"Module '{name}' not found");
    }

    public ValueTask<int> SearcherPreload(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var name = context.GetArgument<string>(0);
        var preload = context.GlobalState.PreloadModules[name];
        if (preload == LuaValue.Nil)
        {
            return new(context.Return());
        }

        return new(context.Return(preload));
    }

    public async ValueTask<int> SearcherLua(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var name = context.GetArgument<string>(0);
        var fileName = await FindFile(context.State, name, "path", context.GlobalState.Platform.FileSystem.DirectorySeparator);
        if (fileName == null)
        {
            return context.Return(LuaValue.Nil);
        }

        return context.Return(await context.State.LoadFileAsync(fileName, "bt", null, cancellationToken));
    }
}