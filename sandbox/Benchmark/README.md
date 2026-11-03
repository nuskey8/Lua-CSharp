# Interpreter benchmarks

The default .NET 10 suite compares Lua-CSharp, MoonSharp 2.0.0 and
[NuLua.Lua52 0.2.0](https://github.com/nuskey8/NuLua). NuLua runs native Lua 5.2.

Run from the repository root:

```powershell
dotnet run -c Release --project sandbox/Benchmark
dotnet run -c Release --project sandbox/Benchmark -- --filter '*InterpreterComparison*'
```

The repository's `global.json` selects a stable .NET 10 SDK for root-level
commands and BenchmarkDotNet's generated builds. SDK selection starts in the
current directory, so a pin only inside the benchmark directory does not cover
root-level `dotnet run --project` commands.

To build and run the executable directly, use this directory:

```powershell
dotnet restore Benchmark.csproj
dotnet build Benchmark.csproj -c Release --no-restore
dotnet bin/Release/net10.0/Benchmark.dll --filter '*InterpreterComparison*' --exporters json
```

## Choosing .NET runtimes

.NET 10 is the default. Select execution runtimes using BenchmarkDotNet's
native option:

```powershell
# .NET 11 runtime only
dotnet run -c Release --project sandbox/Benchmark -- --runtimes net11.0 --filter '*InterpreterComparison*map*'

# Compare .NET 10 and .NET 11 runtimes
dotnet run -c Release --project sandbox/Benchmark -- --runtimes net10.0 net11.0 --filter '*InterpreterComparison*map*'
```

The build stays on .NET 10. The .NET 11 job executes the same `net10.0` code
under the installed .NET 11 runtime, including RC versions. No .NET 11 SDK or
SDK switching is needed. Runtime roll-forward stays within .NET 11.0.

BenchmarkDotNet handles selection, listing, filters, jobs and reports directly.
Use `--job Dry` for a smoke run or `--job Short` for a short measurement run.
The harness uses BenchmarkDotNet 0.16.0-preview.2 for .NET 11 runtime support.

## Layout

- `Comparisons/`: interpreter, compilation and startup comparisons and engine adapters.
- `Workloads/`: case names, script paths and embedded script loading.
- `Scripts/`: Lua sources grouped by suite, embedded in the executable.

Scripts load during setup, outside timed operations, and work from any current
directory. Compiler corpora are stored as complete Lua files.

Cases load their Lua source on demand during setup. Listing benchmarks does
not read Lua scripts.

## Measurement boundaries

| Suite | Timed operation | Cases |
| --- | --- | --- |
| `InterpreterComparison` | Execute a retained compiled chunk and read one numeric result | Numeric loops, Lua calls/varargs, recursive Fibonacci, table access/sort, map, metamethods, math, strings, patterns, coroutines, host callbacks, Mandelbrot, n-body |
| `CompilationComparison` | Compile source in an existing state; no execution | Small loop, n-body, control flow, identifiers, plain strings, escaped strings |
| `StartupComparison` | Create a state, open libraries, dispose where supported | One lifecycle per engine |

The `map` case inserts 1,000 string keys, performs four lookup/update passes,
deletes even keys, checks misses and traverses the remaining entries with
`pairs`. Its order-independent integer checksum is 6,771,000. `mandelbrot`
sums escape iteration counts on a 128 × 96 grid with a 64-iteration limit;
its checksum is 258,320.
Neither workload prints output.

Only the engine being measured is initialized for a benchmark. Execution
excludes source compilation and state setup. Scripts rebuild their mutable
data on each invocation and return a numeric checksum.
Lua-CSharp uses a caller-owned result buffer, NuLua a retained registry
reference and its public stack API, and MoonSharp a retained function value.
The host-callback case uses each engine's direct callback API without reflection
binding. It measures that public embedding path as well as the interpreter.

Compilation uses batches of 64 loads. Each batch starts with a fresh state
outside timing, because MoonSharp retains loaded sources and bytecode in its
state. The native stack is cleared after each load. Each compiler gets two
seconds of bounded warmup. BenchmarkDotNet controls launches and measured
iterations using its normal job options. Source-to-UTF-8 conversion in NuLua's string API is included. Small
batches can be shorter than BenchmarkDotNet's recommended iteration duration;
inspect per-launch variation and confidence intervals.

Startup is a warm-process state lifecycle, including any engine pooling. It
does not measure cold process startup or first-use JIT. MoonSharp has no
equivalent deterministic disposal API.

## Focused runs

```powershell
dotnet bin/Release/net10.0/Benchmark.dll --filter '*InterpreterComparison*host-calls*'
dotnet bin/Release/net10.0/Benchmark.dll --filter '*InterpreterComparison*map*'
dotnet bin/Release/net10.0/Benchmark.dll --filter '*InterpreterComparison*mandelbrot*'
dotnet bin/Release/net10.0/Benchmark.dll --filter '*CompilationComparison*'
dotnet bin/Release/net10.0/Benchmark.dll --filter '*StartupComparison*'
```

`--list flat` lists benchmarks. `--artifacts <directory>` keeps a run's outputs
separate. With no arguments, BenchmarkDotNet opens its benchmark selector.
Use more launches/iterations for small differences. Run timing processes serially on an otherwise idle machine.

GC remains enabled. MemoryDiagnoser reports managed .NET allocation only;
NuLua also allocates on Lua's native heap. Zero managed bytes does not mean
zero total allocation. Keep engine/runtime versions, CPU, tiering/PGO settings,
source revision and workload selection with any published results. Avoid a
single overall ranking without application-specific workload weights.

## Adding workloads

Add an ASCII Lua file under `Scripts/Comparison/` that rebuilds its mutable
data and returns one numeric checksum. Register its name in
`Workloads/ComparisonWorkloads.cs`. The case is
automatically available to all three engines and the NativeAOT execution suite.
Compiler-only sources belong under `Scripts/Compilation/` and are registered
in `CompilationCases`. The project embeds new files automatically.

For Lua-CSharp JIT versus NativeAOT compilation and execution, use the
[NativeAOT benchmarks](../Benchmark.NativeAot/README.md), which share these sources.
