# Focused .NET 10 performance probe

Use this tool for alternating Lua-CSharp revision comparisons, compiler
allocation checks and JIT diagnostics. Run from this directory:

```powershell
dotnet restore
dotnet build -c Release -o ../../artifacts/performance/candidate
dotnet ../../artifacts/performance/candidate/PerformanceProbe.dll --case numeric-for,math
```

The probe loads source once, warms execution, then reports the median of five
timed batches as JSON Lines. Each row includes per-call time/allocation, result
checksum, bytecode length/SHA-256, and separate setup/first-call measurements.
The header records the runtime, engine DLL hash, string mode, tiering/PGO
settings and VM IL statistics. Every sample checks result stability. These
checks detect build differences; they do not independently prove correctness.

`--compile` instead measures repeated source loads in an existing state;
its checksum is instruction count. `--bytes` selects byte strings. Select cases
with `--case name[,name]`; default is all. `--samples`, `--warmup-ms` and
`--sample-ms` control sampling. Invalid/missing options, duplicate cases and
non-positive sample counts/durations return exit code 2. Warmup may be zero.

Sources cover arithmetic/coercion, Lua and native calls, tail calls/varargs,
metamethods, hooks, coroutines, table access/sort/unpack/moves, strings,
patterns, numeric conversion, bit32, and n-body. `format-hex` measures `%a`;
`format-number-quote` measures numeric `%q`. Compiler cases separate keywords,
identifiers, plain strings and escaped strings.

## Comparing revisions

Build both probes from the same source into different output directories.
`LuaAssemblyPath` selects a previously built Release/net10.0 engine without
rebuilding it from the current checkout:

```powershell
dotnet build -c Release -o ../../artifacts/performance/baseline `
  -p:LuaAssemblyPath=C:/absolute/baseline/Lua.dll
./compare.ps1 `
  -Baseline ../../artifacts/performance/baseline/PerformanceProbe.dll `
  -Candidate ../../artifacts/performance/candidate/PerformanceProbe.dll `
  -Output ../../artifacts/performance/comparison -Cases 'math,strings,nbody'
```

The script alternates process order across rounds, restores the caller's PGO
environment, retains raw JSONL and exports CSV medians. It rejects different
measurement/string modes, workload sets, missing/duplicate process results,
and bytecode/checksum differences. Such differences need investigation before
treating a timing change as an optimization.

Use `-Compile` for compiler comparisons and `-Bytes` for byte strings. Profiles
are `pgo-on` (default), `pgo-off`, and `tiered-off`. `-Rounds`, `-WarmupMs` and
`-SampleMs` narrow a run. Run timing processes serially on an otherwise idle
machine. Process-wide GC counters include async continuations and can include
background-runtime allocation noise; small changes need independent repeats.
Setup combines state creation, library opening and compilation; it is not a
parser-only metric. First-call measurements include relevant first-use JIT.

`--benchmark` runs a focused BenchmarkDotNet suite with an in-process no-emit
toolchain. This prevents generated benchmark projects from silently rebuilding
a frozen baseline against current source. Set the process's PGO profile before
launching each binary; it measures warmed execution, not compilation/startup.

## JIT diagnostics

```powershell
$env:DOTNET_TieredCompilation = '0'
$env:DOTNET_JitDisasm = 'Lua.Runtime.LuaVirtualMachine:MoveNext'
$env:DOTNET_JitStdOutFile = 'C:/absolute/candidate-jit.txt'
dotnet ../../artifacts/performance/candidate/PerformanceProbe.dll --case numeric-for
```

Use identical cases/settings for both binaries and restore the environment
after capture. `--eval 'return math.abs(-3)'` executes a diagnostic script and
prints returned values without running timing samples.
