using BenchmarkDotNet.Attributes;
using Lua;
using Lua.Standard;
using MoonSharp.Interpreter;

[Config(typeof(BenchmarkConfig))]
public class NBodyBenchmark
{
    BenchmarkCore core = default!;
    LuaValue[] buffer = new LuaValue[1];

    [IterationSetup]
    public void Setup()
    {
        core = new();
        core.Setup("n-body.lua");
        core.LuaCSharpState.OpenStandardLibraries();
    }

    [IterationCleanup]
    public void Cleanup()
    {
        core.Dispose();
        core = default!;
        GC.Collect();
    }

    // [Benchmark(Description = "MoonSharp (RunString)")]
    // public DynValue Benchmark_MoonSharp_String()
    // {
    //     return core.MoonSharpState.DoString(core.SourceText);
    // }
    //
    // [Benchmark(Description = "MoonSharp (RunFile)")]
    // public DynValue Benchmark_MoonSharp_File()
    // {
    //     return core.MoonSharpState.DoFile(core.FilePath);
    // }

    [Benchmark(Description = "NLua (DoString)", Baseline = true)]
    public object[] Benchmark_NLua_String()
    {
        return core.NLuaState.DoString(core.SourceText);
    }

    // [Benchmark(Description = "NLua (DoFile)")]
    // public object[] Benchmark_NLua_File()
    // {
    //     return core.NLuaState.DoFile(core.FilePath);
    // }

    [Benchmark(Description = "Lua-CSharp (DoString)")]
    public async Task<LuaValue> Benchmark_LuaCSharp_String()
    {
        await core.LuaCSharpState.DoStringAsync(core.SourceText, buffer);
        return buffer[0];
    }

    // [Benchmark(Description = "Lua-CSharp (DoFileAsync)")]
    // public async Task<LuaValue> Benchmark_LuaCSharp_File()
    // {
    //     await core.LuaCSharpState.DoFileAsync(core.FilePath, buffer);
    //     return buffer[0];
    // }
}