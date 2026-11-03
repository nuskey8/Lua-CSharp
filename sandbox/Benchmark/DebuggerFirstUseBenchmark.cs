using BenchmarkDotNet.Attributes;
using Lua;
using Lua.Debugging;
using Lua.Runtime;

[Config(typeof(ComparisonConfig))]
public class DebuggerFirstUseBenchmark
{
    [Params(250, 1000, 2000)]
    public int FunctionCount { get; set; }

    [Params(false, true)]
    public bool BreakpointEnabled { get; set; }

    [Params(false, true)]
    public bool PreRegistered { get; set; }

    LuaState state = default!;
    LuaClosure source = default!;
    LuaClosure driver = default!;
    LuaDebugger debugger = default!;

    [GlobalSetup]
    public void Setup()
    {
        state = LuaState.Create();
        var functions = string.Join(
            "\n",
            Enumerable.Range(1, FunctionCount).Select(i => $"t[{i}]=function() return {i} end")
        );
        source = state.Load(
            "local t={}\n" + functions + "\nlocal function unused()\n return 0\nend\nreturn t",
            "=first-use"
        );
        state.Environment["functions"] = state.ExecuteAsync(source).GetAwaiter().GetResult()[0];
        driver = state.Load($"for i=1,{FunctionCount} do functions[i]() end", "=driver");
    }

    [IterationSetup]
    public void Attach()
    {
        debugger = new LuaDebugger(state);
        debugger.Attach();
        if (PreRegistered)
            debugger.RegisterSource(source);
        if (BreakpointEnabled)
            debugger.SetBreakpoints("=first-use", [FunctionCount + 4]);
    }

    [Benchmark]
    public void FirstCalls() =>
        state.ExecuteAsync(driver, Memory<LuaValue>.Empty).GetAwaiter().GetResult();

    [IterationCleanup]
    public void Detach() => debugger.Dispose();

    [GlobalCleanup]
    public void Cleanup() => state.Dispose();
}
