using BenchmarkDotNet.Attributes;
using Lua;
using Lua.Debugging;
using Lua.Runtime;
using Lua.Standard;

[Config(typeof(ComparisonConfig))]
public class DebuggerBenchmark
{
    [Params("numeric-loop", "calls", "tables", "metamethods", "coroutines", "n-body")]
    public string Workload { get; set; } = "numeric-loop";

    [Params(false, true)]
    public bool Attached { get; set; }

    LuaState state = default!;
    LuaClosure closure = default!;
    LuaDebugger? debugger;

    [GlobalSetup]
    public void Setup()
    {
        state = LuaState.Create();
        state.OpenStandardLibraries();
        var source = Workload switch
        {
            "numeric-loop" => "local x=0;for i=1,10000 do x=x+i end;assert(x==50005000)",
            "calls" =>
                "local function f(x)return x+1 end;local x=0;for i=1,1000 do x=f(x)end;assert(x==1000)",
            "tables" => "local t={x=1};for i=1,1000 do t.x=t.x+1 end;assert(t.x==1001)",
            "metamethods" =>
                "local t=setmetatable({},{__add=function(a,b)return b+1 end});local x;for i=1,1000 do x=t+i end;assert(x==1001)",
            "coroutines" =>
                "local co=coroutine.create(function()for i=1,100 do coroutine.yield(i)end end);local x=0;for i=1,100 do local ok,v=coroutine.resume(co);assert(ok);x=x+v end;assert(x==5050)",
            "n-body" => ReadNBody(),
            _ => throw new InvalidOperationException(),
        };
        closure = state.Load(source, "=debugger-benchmark");
        if (Attached)
        {
            debugger = new LuaDebugger(state);
            debugger.Attach();
        }
        Run();
    }

    [Benchmark]
    public void Run() =>
        state.ExecuteAsync(closure, Memory<LuaValue>.Empty).GetAwaiter().GetResult();

    static string ReadNBody()
    {
        using var source = typeof(DebuggerBenchmark).Assembly.GetManifestResourceStream(
            "Comparison.n-body.lua"
        )!;
        using var reader = new StreamReader(source);
        return reader.ReadToEnd();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        debugger?.Dispose();
        state.Dispose();
    }
}
