using BenchmarkDotNet.Attributes;
using Lua;
using Lua.Debugging;
using Lua.Runtime;

[Config(typeof(ComparisonConfig))]
public class DebuggerControlBenchmark
{
    [Params(1, 2000)]
    public int FunctionCount { get; set; }

    LuaState state = default!;
    LuaClosure source = default!;
    LuaDebugger debugger = default!;

    [GlobalSetup]
    public void Setup()
    {
        state = LuaState.Create();
        debugger = new LuaDebugger(state);
        debugger.Attach();
        source = state.Load(
            "local t={}\n"
                + string.Join(
                    "\n",
                    Enumerable
                        .Range(1, FunctionCount)
                        .Select(i => $"t[{i}]=function() return {i} end")
                )
                + $"\nfor i=1,{FunctionCount} do t[i]() end",
            "=controls"
        );
        state.ExecuteAsync(source, Memory<LuaValue>.Empty).GetAwaiter().GetResult();
    }

    [Benchmark]
    public void PauseAndReset()
    {
        debugger.Pause();
        debugger.Reset();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        debugger.Dispose();
        state.Dispose();
    }
}
