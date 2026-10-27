# .NET 10 JIT and NativeAOT benchmarks

Compare Lua-CSharp source compilation and bytecode execution under .NET 10 JIT
and NativeAOT, using the sources and checksum checks from `../Benchmark`.
NativeAOT compiles the C# interpreter; Lua source is still compiled to Lua
bytecode and interpreted.

Run from this directory:

```powershell
dotnet restore Benchmark.NativeAot.csproj
dotnet build Benchmark.NativeAot.csproj -c Release --no-restore
dotnet bin/Release/net10.0/Benchmark.NativeAot.dll --verify
dotnet bin/Release/net10.0/Benchmark.NativeAot.dll --filter '*RuntimeExecutionComparison*numeric-loop*' --exporters json
dotnet bin/Release/net10.0/Benchmark.NativeAot.dll --filter '*RuntimeCompilationComparison*numeric-loop*' --exporters json
```

Use a case filter before selecting a whole suite. `--list flat` lists the
benchmarks; no arguments opens BenchmarkDotNet's selector. `--artifacts <path>`
keeps outputs separate and `--keepFiles` retains generated projects/executables.

The BenchmarkDotNet NativeAOT toolchain publishes a native benchmark process.
Install the platform's NativeAOT build prerequisites (Visual C++ build tools
and a Windows SDK on Windows). The native job pins compiler/runtime **10.0.14**,
uses speed optimization and the current CPU's native instruction set. Record
the resolved JIT runtime version too. These native executables target the build
machine's CPU capabilities.

`--verify` checks all 15 distinct workloads in the managed host, twice in each
string mode. Each benchmark's setup also runs its checksum checks inside the
actual JIT or NativeAOT benchmark process before timing. Timed execution uses
UTF-16 mode with ASCII inputs, retained state and a compiled chunk. Compilation
has six source cases, a fresh state outside each batch of 64 loads, and no Lua
execution during measurement. Both jobs use three launches. Compilation warms
for two seconds, then measures 20 batches after 20 warmups; execution measures
10 iterations after five warmups with a 250 ms target iteration time.

Run timing jobs serially. JIT uses runtime defaults; NativeAOT has no tiered JIT
or dynamic PGO. Compare the same sources and keep these profiles distinct from
cross-engine results. MemoryDiagnoser reports managed allocation; generated
executable sizes also include BenchmarkDotNet and metadata, so neither is a
minimal embedding application size or total native-Lua memory measurement.
