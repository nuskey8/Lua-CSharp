using System.Reflection;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

if (args.Contains("--verify"))
{
    Console.WriteLine(
        $"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}"
    );
    Console.WriteLine(
        $"MoonSharp: {typeof(MoonSharp.Interpreter.Script).Assembly.GetName().Version}"
    );
    Console.WriteLine(
        $"NuLua Lua52 assembly: {typeof(NuLua.Lua52.Lua52State).Assembly.GetName().Version}"
    );
    InterpreterComparison.Verify();
    return;
}

if (args.Contains("--legacy"))
    BenchmarkSwitcher
        .FromAssembly(Assembly.GetEntryAssembly()!)
        .Run(args.Where(a => a != "--legacy").ToArray());
else
    BenchmarkSwitcher
        .FromTypes([
            typeof(InterpreterComparison),
            typeof(CompilationComparison),
            typeof(StartupComparison),
            typeof(GeneratedUserDataBenchmark),
        ])
        .Run(args);

class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        AddDiagnoser(MemoryDiagnoser.Default);
        AddJob(Job.ShortRun.WithWarmupCount(10).WithIterationCount(10));
    }
}
