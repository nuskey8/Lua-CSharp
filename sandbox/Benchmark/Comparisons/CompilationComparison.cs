using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Lua;
using Lua.Runtime;
using Lua.Standard;
using MoonSharp.Interpreter;
using NuLua.Lua52;

[BenchmarkCategory("Compilation")]
public class CompilationComparison
{
    [ParamsSource(nameof(Cases))]
    public string Case { get; set; } = "numeric-loop";
    public static IEnumerable<string> Cases =>
        ComparisonWorkloads.CompilationCases.Select(c => c.Name);

    string source = default!;
    LuaState lua = default!;
    Script moon = default!;
    Lua52State native = default!;
    const int BatchSize = 64;

    [GlobalSetup(Target = nameof(LuaCSharp))]
    public void SetupLua()
    {
        var workload = ComparisonWorkloads.FindCompilation(Case);
        source = workload.Source;
        WarmCompiler(ResetLua, () => LuaCSharp(), CleanupLua);
    }

    [GlobalSetup(Target = nameof(MoonSharp))]
    public void SetupMoonSharp()
    {
        var workload = ComparisonWorkloads.FindCompilation(Case);
        source = workload.Source;
        WarmCompiler(ResetMoonSharp, () => MoonSharp(), CleanupMoonSharp);
    }

    [GlobalSetup(Target = nameof(NuLua52))]
    public void SetupNuLua()
    {
        var workload = ComparisonWorkloads.FindCompilation(Case);
        source = workload.Source;
        WarmCompiler(ResetNuLua, NuLua52, CleanupNuLua);
    }

    static void WarmCompiler(Action reset, Action compile, Action cleanup)
    {
        // Bounded batches let tiered JIT settle without retaining unbounded source.
        var timer = Stopwatch.StartNew();
        do
        {
            reset();
            try
            {
                compile();
            }
            finally
            {
                cleanup();
            }
        } while (timer.ElapsedMilliseconds < 2000);
    }

    // MoonSharp retains loaded source and bytecode in its Script. A fresh state per
    // bounded batch prevents unbounded growth from distorting compilation timings.
    [IterationSetup(Target = nameof(LuaCSharp))]
    public void ResetLua()
    {
        lua = LuaState.Create();
        lua.OpenStandardLibraries();
    }

    [IterationSetup(Target = nameof(MoonSharp))]
    public void ResetMoonSharp()
    {
        moon = new(CoreModules.Preset_Complete);
    }

    [IterationSetup(Target = nameof(NuLua52))]
    public void ResetNuLua()
    {
        native = Lua52State.Create();
        native.OpenLibraries();
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = BatchSize)]
    public LuaClosure LuaCSharp()
    {
        LuaClosure result = default!;
        for (var i = 0; i < BatchSize; i++)
            result = lua.Load(source, "comparison");
        return result;
    }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public DynValue MoonSharp()
    {
        DynValue result = default!;
        for (var i = 0; i < BatchSize; i++)
            result = moon.LoadString(source, codeFriendlyName: "comparison");
        return result;
    }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public void NuLua52()
    {
        for (var i = 0; i < BatchSize; i++)
        {
            native.LoadString(source, "comparison");
            native.SetTop(0);
        }
    }

    [IterationCleanup(Target = nameof(LuaCSharp))]
    public void CleanupLua() => lua.Dispose();

    [IterationCleanup(Target = nameof(NuLua52))]
    public void CleanupNuLua() => native.Dispose();

    [IterationCleanup(Target = nameof(MoonSharp))]
    public void CleanupMoonSharp() => moon = default!;
}
