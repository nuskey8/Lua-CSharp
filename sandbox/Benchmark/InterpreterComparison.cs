using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using Perfolizer.Horology;

public sealed class ComparisonConfig : ManualConfig
{
    public ComparisonConfig()
    {
        AddDiagnoser(MemoryDiagnoser.Default);
        AddJob(
            Job.ShortRun.WithWarmupCount(3)
                .WithIterationCount(5)
                .WithIterationTime(TimeInterval.FromMilliseconds(250))
        );
        WithOrderer(new DefaultOrderer(SummaryOrderPolicy.Declared));
    }
}

[Config(typeof(ComparisonConfig))]
[BenchmarkCategory("Execution")]
public class InterpreterComparison
{
    [ParamsSource(nameof(Cases))]
    public string Case { get; set; } = "numeric-loop";
    public static IEnumerable<string> Cases => ComparisonWorkloads.Cases.Select(c => c.Name);

    LuaCSharpComparison lua = default!;
    MoonSharpComparison moonSharp = default!;
    NuLua52Comparison nuLua = default!;

    [GlobalSetup(Target = nameof(LuaCSharp))]
    public void SetupLua()
    {
        var workload = ComparisonWorkloads.Find(Case);
        lua = new(workload.Source);
        ComparisonWorkloads.Check(workload, "Lua-CSharp", lua.Run());
    }

    [GlobalSetup(Target = nameof(MoonSharp))]
    public void SetupMoonSharp()
    {
        var workload = ComparisonWorkloads.Find(Case);
        moonSharp = new(workload.Source);
        ComparisonWorkloads.Check(workload, "MoonSharp", moonSharp.Run());
    }

    [GlobalSetup(Target = nameof(NuLua52))]
    public void SetupNuLua()
    {
        var workload = ComparisonWorkloads.Find(Case);
        nuLua = new(workload.Source);
        ComparisonWorkloads.Check(workload, "NuLua Lua 5.2", nuLua.Run());
    }

    [Benchmark(Baseline = true)]
    public double LuaCSharp() => lua.Run();

    [Benchmark]
    public double MoonSharp() => moonSharp.Run();

    [Benchmark]
    public double NuLua52() => nuLua.Run();

    [GlobalCleanup(Target = nameof(LuaCSharp))]
    public void CleanupLua() => lua.Dispose();

    [GlobalCleanup(Target = nameof(NuLua52))]
    public void CleanupNuLua() => nuLua.Dispose();

    public static void Verify()
    {
        ScriptWarmUp();
        foreach (
            var workload in ComparisonWorkloads
                .Cases.Concat(ComparisonWorkloads.CompilationCases)
                .DistinctBy(c => c.Name)
        )
        {
            using var lua = new LuaCSharpComparison(workload.Source);
            using var bytes = new LuaCSharpComparison(workload.Source, bytes: true);
            var moon = new MoonSharpComparison(workload.Source);
            using var native = new NuLua52Comparison(workload.Source);
            // A second invocation detects accidental reuse of mutated workload data.
            for (var i = 0; i < 2; i++)
            {
                ComparisonWorkloads.Check(workload, "Lua-CSharp", lua.Run());
                ComparisonWorkloads.Check(workload, "Lua-CSharp bytes", bytes.Run());
                ComparisonWorkloads.Check(workload, "MoonSharp", moon.Run());
                ComparisonWorkloads.Check(workload, "NuLua Lua 5.2", native.Run());
            }
            Console.WriteLine(
                $"PASS {workload.Category, -12} {workload.Name, -16} {workload.Expected:R}"
            );
        }
    }

    static void ScriptWarmUp() => global::MoonSharp.Interpreter.Script.WarmUp();
}
