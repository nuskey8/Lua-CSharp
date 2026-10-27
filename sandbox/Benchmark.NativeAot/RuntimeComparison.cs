using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.NativeAot;
using Lua;
using Lua.Runtime;
using Lua.Standard;
using Perfolizer.Horology;

static class RuntimeJobs
{
    public static Job NativeAot(Job job) =>
        job.WithRuntime(NativeAotRuntime.Net10_0)
            .WithArguments([
                new MsBuildArgument("/p:RestoreSources=https://api.nuget.org/v3/index.json"),
            ])
            .WithToolchain(
                NativeAotToolchain
                    .CreateBuilder()
                    .UseNuGet("10.0.14", "https://api.nuget.org/v3/index.json")
                    .IlcInstructionSet("native")
                    .TargetFrameworkMoniker("net10.0")
                    .RuntimeFrameworkVersion("10.0.14")
                    .UseNuGetClearTag(true)
                    .DisplayName("NativeAOT 10.0.14")
                    .ToToolchain()
            )
            .WithId("NET10-NativeAOT");
}

public sealed class RuntimeExecutionConfig : ManualConfig
{
    public RuntimeExecutionConfig()
    {
        AddDiagnoser(MemoryDiagnoser.Default);
        var job = Job
            .ShortRun.WithLaunchCount(3)
            .WithWarmupCount(5)
            .WithIterationCount(10)
            .WithIterationTime(TimeInterval.FromMilliseconds(250));
        AddJob(job.WithRuntime(CoreRuntime.Core10_0).WithId("NET10-JIT").AsBaseline());
        AddJob(RuntimeJobs.NativeAot(job));
    }
}

public sealed class RuntimeCompilationConfig : ManualConfig
{
    public RuntimeCompilationConfig()
    {
        AddDiagnoser(MemoryDiagnoser.Default);
        var job = Job
            .ShortRun.WithLaunchCount(3)
            .WithWarmupCount(20)
            .WithIterationCount(20)
            .WithInvocationCount(1)
            .WithUnrollFactor(1);
        AddJob(job.WithRuntime(CoreRuntime.Core10_0).WithId("NET10-JIT").AsBaseline());
        AddJob(RuntimeJobs.NativeAot(job));
    }
}

[Config(typeof(RuntimeExecutionConfig))]
[BenchmarkCategory("Execution")]
public class RuntimeExecutionComparison
{
    [ParamsSource(nameof(Cases))]
    public string Case { get; set; } = "numeric-loop";
    public static IEnumerable<string> Cases => ComparisonWorkloads.Cases.Select(c => c.Name);
    LuaCSharpComparison runner = default!;

    [GlobalSetup]
    public void Setup()
    {
        var workload = ComparisonWorkloads.Find(Case);
        RuntimeVerification.Verify(workload);
        runner = new(workload.Source);
    }

    [Benchmark]
    public double Execute() => runner.Run();

    [GlobalCleanup]
    public void Cleanup() => runner.Dispose();
}

[Config(typeof(RuntimeCompilationConfig))]
[BenchmarkCategory("Compilation")]
public class RuntimeCompilationComparison
{
    [ParamsSource(nameof(Cases))]
    public string Case { get; set; } = "numeric-loop";
    public static IEnumerable<string> Cases =>
        ComparisonWorkloads.CompilationCases.Select(c => c.Name);
    string source = default!;
    LuaState state = default!;
    const int BatchSize = 64;

    [GlobalSetup]
    public void Setup()
    {
        var workload = ComparisonWorkloads.FindCompilation(Case);
        RuntimeVerification.Verify(workload);
        source = workload.Source;
        var timer = Stopwatch.StartNew();
        do
        {
            ResetState();
            try
            {
                Compile();
            }
            finally
            {
                Cleanup();
            }
        } while (timer.ElapsedMilliseconds < 2000);
    }

    [IterationSetup]
    public void ResetState()
    {
        state = LuaState.Create();
        state.OpenStandardLibraries();
    }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public LuaClosure Compile()
    {
        LuaClosure result = default!;
        for (var i = 0; i < BatchSize; i++)
            result = state.Load(source, "comparison");
        return result;
    }

    [IterationCleanup]
    public void Cleanup() => state.Dispose();
}
