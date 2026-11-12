using BenchmarkDotNet.Attributes;
using Lua;
using Lua.Debugging;
using Lua.Runtime;
using Lua.Standard;

[BenchmarkCategory("Debugger")]
[MemoryDiagnoser]
public class DebuggerIdleBenchmark
{
    [Params("numeric-loop", "calls", "coroutines")]
    public string Workload { get; set; } = "numeric-loop";

    /// <summary>An attached debugger with no breakpoints and no pending step must cost nothing per instruction.</summary>
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
        closure = state.Load(Source(Workload), "=debugger-idle");
        if (Attached)
        {
            debugger = new LuaDebugger(state);
            debugger.Attach();
            debugger.RegisterSource(closure);
        }
        Run();
    }

    [Benchmark]
    public void Run() =>
        state.ExecuteAsync(closure, Memory<LuaValue>.Empty).GetAwaiter().GetResult();

    [GlobalCleanup]
    public void Cleanup()
    {
        debugger?.Dispose();
        state.Dispose();
    }

    static string Source(string workload) =>
        workload switch
        {
            "numeric-loop" => "local x=0;for i=1,10000 do x=x+i end;assert(x==50005000)",
            "calls" =>
                "local function f(x)return x+1 end;local x=0;for i=1,1000 do x=f(x)end;assert(x==1000)",
            "coroutines" =>
                "local co=coroutine.create(function()for i=1,100 do coroutine.yield(i)end end);"
                    + "local x=0;for i=1,100 do local ok,v=coroutine.resume(co);assert(ok);x=x+v end;assert(x==5050)",
            _ => throw new InvalidOperationException(),
        };
}

/// <summary>
/// Cost of one stop/continue cycle while every executed prototype is tracked. A breakpoint line
/// inside a loop stops once per iteration, so the per-stop bookkeeping dominates the measurement.
/// </summary>
[BenchmarkCategory("Debugger")]
[MemoryDiagnoser]
public class DebuggerStopBenchmark
{
    const int Stops = 500;

    /// <summary>Number of additional functions executed before the loop, each with 40 source lines.</summary>
    [Params(0, 2000)]
    public int ActivatedFunctions { get; set; }

    LuaState state = default!;
    LuaClosure closure = default!;
    LuaDebugger debugger = default!;
    int observedStops;

    [GlobalSetup]
    public void Setup()
    {
        state = LuaState.Create();
        state.OpenStandardLibraries();
        debugger = new LuaDebugger(state);
        debugger.Attach();
        var source = BuildSource(ActivatedFunctions);
        closure = state.Load(source, "=debugger-stop");
        debugger.RegisterSource(closure);
        debugger.SetBreakpoints("=debugger-stop", [source.Split('\n').Length - 3]);
        debugger.Stopped += stop =>
        {
            observedStops++;
            debugger.Continue(stop.Id);
        };
        Run();
    }

    [Benchmark]
    public int Run()
    {
        observedStops = 0;
        state.ExecuteAsync(closure, Memory<LuaValue>.Empty).GetAwaiter().GetResult();
        return observedStops;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        debugger.Dispose();
        state.Dispose();
    }

    static string BuildSource(int functions)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("local fs = {}\n");
        for (var i = 0; i < functions; i++)
        {
            builder.Append("fs[").Append(i).Append("] = function(x)\n");
            for (var line = 0; line < 40; line++)
                builder.Append("  x = x + ").Append(line).Append('\n');
            builder.Append("  return x\nend\n");
        }
        for (var i = 0; i < functions; i++)
            builder.Append("fs[").Append(i).Append("](1)\n");
        builder.Append("local n = 0\n");
        builder.Append("for i = 1, ").Append(Stops).Append(" do\n");
        builder.Append("  n = n + 1\n");
        builder.Append("end\n");
        builder.Append("return n\n");
        return builder.ToString();
    }
}
