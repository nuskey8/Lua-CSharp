# Interpreter benchmarks

The default .NET 10 suite compares Lua-CSharp, MoonSharp 2.0.0 and
[NuLua.Lua52 0.1.0](https://github.com/nuskey8/NuLua). NuLua runs native Lua 5.2;
setup checks `_VERSION`. NLua remains available in the legacy benchmarks.

Run from this directory so `global.json` selects the .NET 10 SDK:

```powershell
dotnet restore Benchmark.csproj
dotnet build Benchmark.csproj -c Release --no-restore
dotnet bin/Release/net10.0/Benchmark.dll --verify
dotnet bin/Release/net10.0/Benchmark.dll --filter '*InterpreterComparison*' --exporters json
```

`--verify` checks independent numeric results twice on all three engines and
both Lua-CSharp string modes, including the compiler workloads. Timed runs use
Lua-CSharp's default UTF-16 mode. Shared sources are ASCII; these cases do not
measure Unicode encoding or arbitrary binary strings.

## Measurement boundaries

| Suite | Timed operation | Cases |
| --- | --- | --- |
| `InterpreterComparison` | Execute a retained compiled chunk and read one numeric result | Numeric loops, Lua calls/varargs, table access/sort, metamethods, math, strings, patterns, coroutines, host callbacks, n-body |
| `CompilationComparison` | Compile source in an existing state; no execution | Small loop, n-body, control flow, identifiers, plain strings, escaped strings |
| `StartupComparison` | Create a state, open libraries, dispose where supported | One lifecycle per engine |

Only the engine being measured is initialized for a benchmark. Execution
excludes source compilation and state setup. Scripts rebuild their mutable
data on each invocation; each has an independently calculated checksum.
Lua-CSharp uses a caller-owned result buffer, NuLua a retained registry
reference and its public stack API, and MoonSharp a retained function value.
The host-callback case uses each engine's direct callback API without reflection
binding. It measures that public embedding path as well as the interpreter.

Compilation uses batches of 64 loads. Each batch starts with a fresh state
outside timing, because MoonSharp retains loaded sources and bytecode in its
state. The native stack is cleared after each load. Each compiler gets two
seconds of bounded warmup, then three launches with 20 warmup and 20 measured
batches. Source-to-UTF-8 conversion in NuLua's string API is included. Small
batches can be shorter than BenchmarkDotNet's recommended iteration duration;
inspect per-launch variation and confidence intervals.

Startup is a warm-process state lifecycle, including any engine pooling. It
does not measure cold process startup or first-use JIT. MoonSharp has no
equivalent deterministic disposal API.

## Focused runs

```powershell
dotnet bin/Release/net10.0/Benchmark.dll --filter '*InterpreterComparison*host-calls*'
dotnet bin/Release/net10.0/Benchmark.dll --filter '*CompilationComparison*'
dotnet bin/Release/net10.0/Benchmark.dll --filter '*StartupComparison*'
dotnet bin/Release/net10.0/Benchmark.dll --legacy --filter '*GSubBenchmark*'
```

`--list flat` lists benchmarks. `--artifacts <directory>` keeps a run's outputs
separate. With no arguments, BenchmarkDotNet opens its benchmark selector.
Execution/startup defaults use three warmups and five measurements
with a 250 ms target iteration time. Use more launches/iterations for small
differences. Run timing processes serially on an otherwise idle machine.

GC remains enabled. MemoryDiagnoser reports managed .NET allocation only;
NuLua also allocates on Lua's native heap. Zero managed bytes does not mean
zero total allocation. Keep engine/runtime versions, CPU, tiering/PGO settings,
source revision and workload selection with any published results. Avoid a
single overall ranking without application-specific workload weights.

The legacy suites include file I/O, source execution and focused API tests with
different boundaries. Their results should not be mixed with this suite.

For Lua-CSharp JIT versus NativeAOT compilation and execution, use the
[NativeAOT benchmarks](../Benchmark.NativeAot/README.md), which share these sources.
