using BenchmarkDotNet.Attributes;

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
    }

    [GlobalSetup(Target = nameof(MoonSharp))]
    public void SetupMoonSharp()
    {
        var workload = ComparisonWorkloads.Find(Case);
        moonSharp = new(workload.Source);
    }

    [GlobalSetup(Target = nameof(NuLua52))]
    public void SetupNuLua()
    {
        var workload = ComparisonWorkloads.Find(Case);
        nuLua = new(workload.Source);
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
}
