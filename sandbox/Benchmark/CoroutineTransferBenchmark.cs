using BenchmarkDotNet.Attributes;
using Lua;
using Lua.Runtime;
using Lua.Standard;

[Config(typeof(ComparisonConfig))]
public class CoroutineTransferBenchmark
{
    [Params("resume", "wrap")]
    public string Operation { get; set; } = "resume";

    [Params(1, 64, 4096)]
    public int ResultCount { get; set; }

    [Params(false, true)]
    public bool Cancellable { get; set; }

    LuaState state = default!;
    LuaClosure closure = default!;
    CancellationTokenSource cancellation = default!;

    [GlobalSetup]
    public void Setup()
    {
        state = LuaState.Create();
        state.OpenStandardLibraries();
        cancellation = new();
        state.Environment["packet"] = new LuaFunction(
            (context, _) =>
            {
                context.GetReturnBuffer(ResultCount).Fill(42);
                return new(ResultCount);
            }
        );
        closure = state.Load(
            Operation == "wrap"
                ? "local run=coroutine.wrap(function() for i=1,64 do coroutine.yield(packet()) end end); for i=1,64 do run() end; run(); return 42"
                : "local co=coroutine.create(function() for i=1,64 do coroutine.yield(packet()) end end); for i=1,64 do coroutine.resume(co) end; coroutine.resume(co); return 42",
            "coroutine-transfer"
        );
        if (Run() != 42)
            throw new InvalidOperationException("Unexpected coroutine transfer result");
    }

    [Benchmark(OperationsPerInvoke = 64)]
    public int Run()
    {
        state
            .RunAsync(closure, 0, Cancellable ? cancellation.Token : default)
            .GetAwaiter()
            .GetResult();
        return state.Stack.Pop().Read<int>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        cancellation.Dispose();
        state.Dispose();
    }
}
