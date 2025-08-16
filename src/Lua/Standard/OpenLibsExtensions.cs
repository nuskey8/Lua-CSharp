using Lua.IO;
using Lua.Runtime;

namespace Lua.Standard;

public static class OpenLibsExtensions
{
    public static void OpenBasicLibrary(this LuaState state)
    {
        var globalState = state.GlobalState;
        globalState.Environment["_G"] = globalState.Environment;
        globalState.Environment["_VERSION"] = "Lua 5.2";
        foreach (var func in BasicLibrary.Instance.Functions)
        {
            globalState.Environment[func.Name] = func;
        }
    }

    public static void OpenBitwiseLibrary(this LuaState state)
    {
        var globalState = state.GlobalState;

        LuaTable bit32 = new(0, BitwiseLibrary.Instance.Functions.Length);
        foreach (var func in BitwiseLibrary.Instance.Functions)
        {
            bit32[func.Name] = func.Func;
        }

        globalState.Environment["bit32"] = bit32;
        globalState.LoadedModules["bit32"] = bit32;
    }

    public static void OpenCoroutineLibrary(this LuaState state)
    {
        var globalState = state.GlobalState;
        LuaTable coroutine = new(0, CoroutineLibrary.Instance.Functions.Length);
        foreach (var func in CoroutineLibrary.Instance.Functions)
        {
            coroutine[func.Name] = func.Func;
        }

        globalState.Environment["coroutine"] = coroutine;
    }

    public static void OpenIOLibrary(this LuaState state)
    {
        var globalState = state.GlobalState;
        LuaTable io = new(0, IOLibrary.Instance.Functions.Length);
        foreach (var func in IOLibrary.Instance.Functions)
        {
            io[func.Name] = func.Func;
        }

        var registry = globalState.Registry;
        var standardIO = globalState.Platform.StandardIO;
        LuaValue stdin = new(new FileHandle(standardIO.Input));
        LuaValue stdout = new(new FileHandle(standardIO.Output));
        LuaValue stderr = new(new FileHandle(standardIO.Error));
        registry["_IO_input"] = stdin;
        registry["_IO_output"] = stdout;
        io["stdin"] = stdin;
        io["stdout"] = stdout;
        io["stderr"] = stderr;

        globalState.Environment["io"] = io;
        globalState.LoadedModules["io"] = io;
    }

    public static void OpenMathLibrary(this LuaState state)
    {
        var globalState = state.GlobalState;
        globalState.Environment[MathematicsLibrary.RandomInstanceKey] = new(new MathematicsLibrary.RandomUserData(new()));

        LuaTable math = new(0, MathematicsLibrary.Instance.Functions.Length);
        foreach (var func in MathematicsLibrary.Instance.Functions)
        {
            math[func.Name] = func.Func;
        }

        math["pi"] = Math.PI;
        math["huge"] = double.PositiveInfinity;

        globalState.Environment["math"] = math;
        globalState.LoadedModules["math"] = math;
    }

    public static void OpenModuleLibrary(this LuaState state)
    {
        var globalState = state.GlobalState;
        LuaTable package = new(0, 8);
        package["loaded"] = globalState.LoadedModules;
        package["preload"] = globalState.PreloadModules;
        var moduleLibrary = ModuleLibrary.Instance;
        LuaTable searchers = new();
        searchers[1] = new LuaFunction("preload", moduleLibrary.SearcherPreload);
        searchers[2] = new LuaFunction("searcher_Lua", moduleLibrary.SearcherLua);
        package["searchers"] = searchers;
        package["path"] = "?.lua";
        package["searchpath"] = moduleLibrary.SearchPathFunction;
        package["config"] = $"{Path.DirectorySeparatorChar}\n;\n?\n!\n-";
        globalState.Environment["package"] = package;
        globalState.Environment["require"] = moduleLibrary.RequireFunction;
    }

    public static void OpenOperatingSystemLibrary(this LuaState state)
    {
        var globalState = state.GlobalState;
        LuaTable os = new(0, OperatingSystemLibrary.Instance.Functions.Length);
        foreach (var func in OperatingSystemLibrary.Instance.Functions)
        {
            os[func.Name] = func.Func;
        }

        globalState.Environment["os"] = os;
        globalState.LoadedModules["os"] = os;
    }

    public static void OpenStringLibrary(this LuaState state)
    {
        var globalState = state.GlobalState;
        LuaTable @string = new(0, StringLibrary.Instance.Functions.Length);
        foreach (var func in StringLibrary.Instance.Functions)
        {
            @string[func.Name] = func.Func;
        }

        globalState.Environment["string"] = @string;
        globalState.LoadedModules["string"] = @string;

        // set __index
        LuaValue key = new("");
        if (!globalState.TryGetMetatable(key, out var metatable))
        {
            metatable = new();
            globalState.SetMetatable(key, metatable);
        }

        metatable[Metamethods.Index] = new LuaFunction("index", (context, cancellationToken) =>
        {
            context.GetArgument<string>(0);
            var key = context.GetArgument(1);
            return new(context.Return(@string[key]));
        });
    }

    public static void OpenTableLibrary(this LuaState state)
    {
        var globalState = state.GlobalState;
        LuaTable table = new(0, TableLibrary.Instance.Functions.Length);
        foreach (var func in TableLibrary.Instance.Functions)
        {
            table[func.Name] = func.Func;
        }

        globalState.Environment["table"] = table;
        globalState.LoadedModules["table"] = table;
    }


    public static void OpenDebugLibrary(this LuaState state)
    {
        var globalState = state.GlobalState;
        LuaTable debug = new(0, DebugLibrary.Instance.Functions.Length);
        foreach (var func in DebugLibrary.Instance.Functions)
        {
            debug[func.Name] = func.Func;
        }

        globalState.Environment["debug"] = debug;
        globalState.LoadedModules["debug"] = debug;
    }

    public static void OpenStandardLibraries(this LuaState state)
    {
        state.OpenBasicLibrary();
        state.OpenBitwiseLibrary();
        state.OpenCoroutineLibrary();
        state.OpenIOLibrary();
        state.OpenMathLibrary();
        state.OpenModuleLibrary();
        state.OpenOperatingSystemLibrary();
        state.OpenStringLibrary();
        state.OpenTableLibrary();
        state.OpenDebugLibrary();
    }
}