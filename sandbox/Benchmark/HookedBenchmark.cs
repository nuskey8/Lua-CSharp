using BenchmarkDotNet.Attributes;
using Lua;
using Lua.Standard;

[Config(typeof(BenchmarkConfig))]
public class HookedBenchmark
{
    BenchmarkCore core = default!;
    LuaValue[] buffer = new LuaValue[1];

    [IterationSetup]
    public void Setup()
    {
        core = new();
        core.Setup("hooked.lua");
        core.LuaCSharpState.OpenStandardLibraries();
    }

    [IterationCleanup]
    public void Cleanup()
    {
        core.Dispose();
        core = default!;
        GC.Collect();
    }


    [Benchmark(Description = "NLua (DoString)", Baseline = true)]
    public object[] Benchmark_NLua_String()
    {
        return core.NLuaState.DoString(core.SourceText);
    }

    [Benchmark(Description = "Lua-CSharp (DoString)")]
    public async Task<LuaValue> Benchmark_LuaCSharp_String()
    {
        await core.LuaCSharpState.DoStringAsync(core.SourceText, buffer);
        return buffer[0];
    }
}