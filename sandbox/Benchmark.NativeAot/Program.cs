using BenchmarkDotNet.Running;

if (args.Contains("--verify"))
{
    foreach (
        var workload in ComparisonWorkloads
            .Cases.Concat(ComparisonWorkloads.CompilationCases)
            .DistinctBy(c => c.Name)
    )
        RuntimeVerification.Verify(workload);
    return;
}

BenchmarkSwitcher
    .FromTypes([typeof(RuntimeExecutionComparison), typeof(RuntimeCompilationComparison)])
    .Run(args);

static class RuntimeVerification
{
    public static void Verify(ComparisonCase workload)
    {
        foreach (var bytes in new[] { false, true })
        {
            using var runner = new LuaCSharpComparison(workload.Source, bytes);
            for (var i = 0; i < 2; i++)
                ComparisonWorkloads.Check(
                    workload,
                    bytes ? "Lua-CSharp bytes" : "Lua-CSharp",
                    runner.Run()
                );
        }
        Console.WriteLine($"PASS {workload.Name}: {workload.Expected:R}");
    }
}
