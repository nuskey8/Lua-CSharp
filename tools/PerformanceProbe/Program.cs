using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using Lua;
using Lua.Runtime;

if (args.Contains("--benchmark"))
{
    // Run in this process so a probe built against a baseline Lua.dll cannot
    // silently rebuild its benchmarks against the current working tree.
    var config = DefaultConfig.Instance.AddJob(
        Job.ShortRun.WithToolchain(InProcessNoEmitToolchain.Instance)
    );
    BenchmarkSwitcher
        .FromTypes([typeof(InterpreterBenchmarks)])
        .Run(args.Where(a => a != "--benchmark").ToArray(), config);
    return 0;
}

var options = new Dictionary<string, string>(StringComparer.Ordinal);
var switches = new HashSet<string>(StringComparer.Ordinal);
string[] cases;
int samples,
    warmup,
    duration;
try
{
    for (var i = 0; i < args.Length; i++)
    {
        var name = args[i];
        if (name is "--bytes" or "--compile")
        {
            if (!switches.Add(name))
                throw new ArgumentException($"Duplicate option: {name}");
        }
        else if (name is "--case" or "--samples" or "--warmup-ms" or "--sample-ms" or "--eval")
        {
            if (++i == args.Length)
                throw new ArgumentException($"Missing value for {name}");
            if (!options.TryAdd(name, args[i]))
                throw new ArgumentException($"Duplicate option: {name}");
        }
        else
            throw new ArgumentException($"Unknown option: {name}");
    }

    var selected = options.GetValueOrDefault("--case", "all");
    cases = selected == "all" ? Workload.Sources.Keys.ToArray() : selected.Split(',');
    if (cases.Distinct(StringComparer.Ordinal).Count() != cases.Length)
        throw new ArgumentException("Duplicate workload names");
    foreach (var name in cases)
        if (!Workload.Sources.ContainsKey(name))
            throw new ArgumentException($"Unknown workload: {name}");
    samples = ReadCount("--samples", 5, 1);
    warmup = ReadCount("--warmup-ms", 300, 0);
    duration = ReadCount("--sample-ms", 60, 1);
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine(
        "Usage: PerformanceProbe [--case name[,name]] [--samples n] [--warmup-ms n] [--sample-ms n] [--bytes] [--compile] | --eval source | --benchmark [BenchmarkDotNet options]"
    );
    return 2;
}

int ReadCount(string name, int fallback, int minimum)
{
    if (!options.TryGetValue(name, out var text))
        return fallback;
    if (
        !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
        || value < minimum
    )
        throw new ArgumentException($"{name} must be an integer >= {minimum}");
    return value;
}

if (options.TryGetValue("--eval", out var source))
{
    using var state = Lua.LuaState.Create();
    Lua.Standard.OpenLibsExtensions.OpenStandardLibraries(state);
    var values = state.DoStringAsync(source).AsTask().GetAwaiter().GetResult();
    Console.WriteLine(JsonSerializer.Serialize(values.Select(v => v.ToString()).ToArray()));
    return 0;
}
var method = typeof(LuaVirtualMachine).GetMethod(
    "MoveNext",
    BindingFlags.Static | BindingFlags.NonPublic
)!;
var body = method.GetMethodBody()!;
Console.WriteLine(
    JsonSerializer.Serialize(
        new
        {
            runtime = RuntimeInformation.FrameworkDescription,
            vmIlBytes = body.GetILAsByteArray()!.Length,
            vmLocals = body.LocalVariables.Count,
            vmEhRegions = body.ExceptionHandlingClauses.Count,
            pgo = Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
            tiered = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            mode = args.Contains("--compile") ? "compilation" : "execution",
            stringMode = switches.Contains("--bytes") ? "bytes" : "utf16",
            luaAssemblySha256 = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(typeof(LuaState).Assembly.Location))
            ),
        }
    )
);

foreach (var name in cases)
{
    var setupTimer = Stopwatch.StartNew();
    var setupAllocated = GC.GetTotalAllocatedBytes(precise: true);
    using var workload = new Workload(name, args.Contains("--bytes"), args.Contains("--compile"));
    setupAllocated = GC.GetTotalAllocatedBytes(precise: true) - setupAllocated;
    var setupNs = setupTimer.Elapsed.TotalNanoseconds;
    var bytecode = workload.Closure.Proto.ToBytecode();
    var firstAllocated = GC.GetTotalAllocatedBytes(precise: true);
    setupTimer.Restart();
    var expected = workload.Run();
    var firstExecutionNs = setupTimer.Elapsed.TotalNanoseconds;
    firstAllocated = GC.GetTotalAllocatedBytes(precise: true) - firstAllocated;
    var timer = Stopwatch.StartNew();
    do
    {
        workload.Run();
    } while (timer.ElapsedMilliseconds < warmup);
    var times = new double[samples];
    var allocations = new double[samples];
    for (var sample = 0; sample < samples; sample++)
    {
        // Total allocated bytes also covers work resumed on other threads.
        var before = GC.GetTotalAllocatedBytes(precise: true);
        long count = 0;
        timer.Restart();
        do
        {
            var result = workload.Run();
            if (result != expected)
                throw new InvalidOperationException(
                    $"Unstable result for {name}: {result} != {expected}"
                );
            count++;
        } while (timer.ElapsedMilliseconds < duration);
        var elapsed = timer.Elapsed.TotalNanoseconds;
        allocations[sample] = (GC.GetTotalAllocatedBytes(precise: true) - before) / (double)count;
        times[sample] = elapsed / count;
    }
    Array.Sort(times);
    Array.Sort(allocations);
    Console.WriteLine(
        JsonSerializer.Serialize(
            new
            {
                name,
                ns = times[samples / 2],
                bytes = allocations[samples / 2],
                checksum = expected,
                bytecodeBytes = bytecode.Length,
                bytecodeHash = Convert.ToHexString(SHA256.HashData(bytecode)),
                setupNs,
                setupAllocated,
                firstExecutionNs,
                firstAllocated,
            }
        )
    );
}
return 0;

[MemoryDiagnoser]
public class InterpreterBenchmarks
{
    [Params(
        "numeric-for",
        "numeric-eq",
        "comparison-meta",
        "concat-pair",
        "concat-many",
        "api-add-sync",
        "api-add-async",
        "arithmetic",
        "lua-calls",
        "math",
        "strings",
        "string-rep",
        "string-sub",
        "table-concat",
        "table-sort",
        "patterns",
        "nbody"
    )]
    public string Case { get; set; } = "numeric-for";
    Workload workload = null!;

    [GlobalSetup]
    public void Setup() => workload = new Workload(Case);

    [GlobalCleanup]
    public void Cleanup() => workload.Dispose();

    [Benchmark]
    public double Execute() => workload.Run();
}
