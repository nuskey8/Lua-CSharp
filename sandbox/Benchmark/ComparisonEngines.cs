using MoonSharp.Interpreter;
using NuLua;
using NuLua.Lua52;

public sealed class MoonSharpComparison
{
    readonly Script state = new(CoreModules.Preset_Complete);
    readonly DynValue closure;

    public MoonSharpComparison(string source)
    {
        state.Globals["host_add"] = DynValue.NewCallback(
            (context, args) => DynValue.NewNumber(args[0].Number + args[1].Number)
        );
        closure = state.LoadString(source, codeFriendlyName: "comparison");
    }

    public double Run() => state.Call(closure).Number;
}

public sealed class NuLua52Comparison : IDisposable
{
    readonly Lua52State state;
    readonly LuaReference closure;

    public NuLua52Comparison(string source)
    {
        state = Lua52State.Create();
        state.OpenLibraries();
        state.GetGlobal("_VERSION");
        if (state.ToString(-1) != "Lua 5.2")
            throw new InvalidOperationException("Expected native Lua 5.2");
        state.SetTop(0);
        state.RegisterFunction(
            "host_add",
            (vm, args) =>
            {
                vm.PushNumber(args[0].Read<double>() + args[1].Read<double>());
                return 1;
            }
        );
        state.LoadString(source, "comparison");
        closure = state.Ref(state.RegistryIndex);
    }

    public double Run()
    {
        // Use the public stack API and a retained registry reference, avoiding a results
        // array just as Lua-CSharp uses a caller-owned return buffer.
        state.PushValue(closure);
        state.Call(0, 1);
        var result = state.ToNumber(-1);
        state.SetTop(0);
        return result;
    }

    public void Dispose()
    {
        state.Unref(closure);
        state.Dispose();
    }
}
