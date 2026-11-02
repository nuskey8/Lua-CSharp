using BenchmarkDotNet.Attributes;
using Lua;
using Lua.Debugging;
using Lua.Runtime;
using Lua.Standard;

[Config(typeof(ComparisonConfig))]
public class DebuggerBreakpointBenchmark
{
    [Params("other-function", "cold-branch")]
    public string Location { get; set; } = "other-function";

    [Params(false, true)]
    public bool BreakpointEnabled { get; set; }

    LuaState state = default!;
    LuaClosure closure = default!;
    LuaDebugger debugger = default!;

    [GlobalSetup]
    public void Setup()
    {
        state = LuaState.Create();
        state.OpenStandardLibraries();
        debugger = new LuaDebugger(state);
        debugger.Attach();
        closure = state.Load(
            Location == "other-function"
                ? "local function unused()\n return 0\nend\nlocal x=0;for i=1,10000 do x=x+i end;assert(x==50005000)"
                : "local x=0\nfor i=1,10000 do x=x+i end\nif x<0 then\n x=0\nend\nassert(x==50005000)",
            "=breakpoint-benchmark"
        );
        debugger.RegisterSource(closure);
        if (BreakpointEnabled)
            debugger.SetBreakpoints(
                "=breakpoint-benchmark",
                [Location == "other-function" ? 2 : 4]
            );
        Run();
    }

    [Benchmark]
    public void Run() =>
        state.ExecuteAsync(closure, Memory<LuaValue>.Empty).GetAwaiter().GetResult();

    [GlobalCleanup]
    public void Cleanup()
    {
        debugger.Dispose();
        state.Dispose();
    }
}
