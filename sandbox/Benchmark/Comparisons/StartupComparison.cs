using BenchmarkDotNet.Attributes;
using Lua;
using Lua.Standard;
using MoonSharp.Interpreter;
using NuLua.Lua52;

[BenchmarkCategory("Startup")]
public class StartupComparison
{
    [Benchmark(Baseline = true)]
    public void LuaCSharp()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
    }

    [Benchmark]
    public Script MoonSharp() => new(CoreModules.Preset_Complete);

    [Benchmark]
    public void NuLua52()
    {
        using var state = Lua52State.Create();
        state.OpenLibraries();
    }
}
