using System.Runtime.CompilerServices;
using System.Text;
using Lua.Internal.CompilerServices;

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

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> Require(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var arg0 = context.GetStringArgument(0);
        var loaded = context.GlobalState.LoadedModules;

        var loadedTable = loaded[arg0];
        if (!loadedTable.ToBoolean())
        {
            LuaFunction loader;
            LuaValue data = LuaValue.Nil;
            var moduleLoader = context.GlobalState.ModuleLoader;
            var hostName = moduleLoader is null ? arg0 : context.State.Platform.DecodeText(arg0);
            if (moduleLoader != null && moduleLoader.Exists(hostName))
            {
                var module = await moduleLoader.LoadAsync(hostName, cancellationToken);
                loader = context.State.Load(module);
            }
            else
            {
                (loader, data) = await FindLoader(context.State, arg0, cancellationToken);
            }

            context.State.Stack.PushRange(arg0, data);
            var count = await context.State.RunAsync(
                loader,
                2,
                context.ReturnFrameBase,
                cancellationToken
            );
            if (
                count > 0
                && context.State.Stack.Get(context.ReturnFrameBase).Type != LuaValueType.Nil
            )
                loaded[arg0] = context.State.Stack.Get(context.ReturnFrameBase);
            loadedTable = loaded[arg0];
            if (loadedTable.Type == LuaValueType.Nil)
                loaded[arg0] = loadedTable = true;
        }

        return context.Return(loadedTable);
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    internal static async ValueTask<(string? FileName, string? Error)> FindFile(
        LuaState state,
        string name,
        string pName,
        string dirSeparator
    )
    {
        var globalState = state.GlobalState;
        var package = globalState.Environment["package"];
        var p = await state.GetTableAsync(package, pName);
        if (p.Type is not (LuaValueType.String or LuaValueType.Number))
        {
            throw new LuaRuntimeException(state, $"package.{pName} must be a string");
        }

        return SearchPath(
            state,
            name,
            p.ToLuaString(state.Platform.NumberFormat),
            ".",
            dirSeparator
        );
    }

    public ValueTask<int> SearchPath(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var name = context.GetStringArgument(0);
        var path = context.GetStringArgument(1);
        var separator = context.HasArgument(2) ? context.GetStringArgument(2) : ".";
        var dirSeparator = context.HasArgument(3)
            ? context.GetStringArgument(3)
            : context.State.Platform.FileSystem.DirectorySeparator;
        var (fileName, error) = SearchPath(context.State, name, path, separator, dirSeparator);
        return new(
            fileName != null ? context.Return(fileName) : context.Return(LuaValue.Nil, error!)
        );
    }

    internal static (string? FileName, string? Error) SearchPath(
        LuaState state,
        string name,
        string path,
        string separator,
        string dirSeparator
    )
    {
        if (separator != "")
        {
            name = name.Replace(separator, dirSeparator);
        }

        var pathSpan = path.AsSpan();
        StringBuilder? errors = null;
        while (!pathSpan.IsEmpty)
        {
            var nextIndex = pathSpan.IndexOf(';');
            if (nextIndex < 0)
                nextIndex = pathSpan.Length;
            var template = pathSpan[..nextIndex];
            if (!template.IsEmpty)
            {
                var fileName = template.ToString().Replace("?", name);
                if (state.Platform.FileSystem.IsReadable(state.Platform.DecodeText(fileName)))
                    return (fileName, null);
                (errors ??= new()).Append("\n\tno file '").Append(fileName).Append('\'');
            }
            pathSpan = nextIndex == pathSpan.Length ? default : pathSpan[(nextIndex + 1)..];
        }

        return (null, errors?.ToString() ?? "");
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    internal static async ValueTask<(LuaFunction Function, LuaValue Data)> FindLoader(
        LuaState state,
        string name,
        CancellationToken cancellationToken
    )
    {
        var package = state.GlobalState.Environment["package"].Read<LuaTable>();
        var searchers = package["searchers"].Read<LuaTable>();
        StringBuilder? errors = null;
        for (var i = 1; ; i++)
        {
            var searcher = searchers[i];
            if (searcher.Type == LuaValueType.Nil)
                break;

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
                    var data = resultCount > 1 ? state.Stack.Get(top + 1) : LuaValue.Nil;
                    state.Stack.SetTop(top);
                    return (result.Read<LuaFunction>(), data);
                }
                if (result.Type is LuaValueType.String or LuaValueType.Number)
                    (errors ??= new()).Append(result.ToLuaString(state.Platform.NumberFormat));
            }

            state.Stack.SetTop(top);
        }

        throw new LuaRuntimeException(state, $"module '{name}' not found:{errors}");
    }

    public ValueTask<int> SearcherPreload(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var name = context.GetStringArgument(0);
        var preload = context.GlobalState.PreloadModules[name];
        if (preload == LuaValue.Nil)
        {
            return new(context.Return($"\n\tno field package.preload['{name}']"));
        }

        return new(context.Return(preload));
    }

    public async ValueTask<int> SearcherLua(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var name = context.GetStringArgument(0);
        var (fileName, error) = await FindFile(
            context.State,
            name,
            "path",
            context.GlobalState.Platform.FileSystem.DirectorySeparator
        );
        if (fileName == null)
        {
            return context.Return(error!);
        }

        return context.Return(
            await context.State.LoadFileAsync(
                context.State.Platform.DecodeText(fileName),
                "bt",
                null,
                cancellationToken
            ),
            fileName
        );
    }
}
