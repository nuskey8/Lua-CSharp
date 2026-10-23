using System.Threading.Tasks.Sources;
using BenchmarkDotNet.Attributes;
using Lua;
using Lua.Runtime;
using Lua.Standard;

[Config(typeof(BenchmarkConfig))]
public class GSubBenchmark
{
    [Params(
        "literal",
        "capture",
        "position",
        "frontier",
        "function",
        "async-function",
        "async-index"
    )]
    public string Replacement { get; set; } = "literal";

    LuaState state = default!;
    LuaClosure closure = default!;
    readonly LuaValue[] buffer = new LuaValue[2];
    readonly SuspendedReplacement suspension = new();

    [GlobalSetup]
    public void Setup()
    {
        state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["source"] = new string('a', 64);
        state.Environment["suspend"] = new LuaFunction(
            "suspend",
            (context, _) => suspension.Start(context.Return("R"))
        );
        state
            .DoStringAsync(
                "repl = setmetatable({}, {__index = suspend}); sync = function() return 'R' end"
            )
            .GetAwaiter()
            .GetResult();
        var script = Replacement switch
        {
            "literal" => "return string.gsub(source, 'a', 'R')",
            "capture" => "return string.gsub(source, '(a)', '[%1]%0%%')",
            "position" => "return string.gsub(source, '()a', '%1')",
            "frontier" => "return string.gsub(source, '%f[%z]', '.')",
            "function" => "return string.gsub('a', '.', sync)",
            "async-function" => "return string.gsub('a', '.', suspend)",
            "async-index" => "return string.gsub('a', '.', repl)",
            _ => throw new ArgumentOutOfRangeException(nameof(Replacement)),
        };
        closure = state.Load(script, "gsub-benchmark");
    }

    [GlobalCleanup]
    public void Cleanup() => state.Dispose();

    [Benchmark]
    public int GSub()
    {
        var task = state.ExecuteAsync(closure, buffer);
        // Suspend and resume on this thread so allocation measurements include all
        // of gsub, without measuring Task.Yield or thread-pool scheduling costs.
        while (!task.IsCompleted)
        {
            suspension.Complete();
        }

        return task.GetAwaiter().GetResult();
    }

    sealed class SuspendedReplacement : IValueTaskSource<int>
    {
        ManualResetValueTaskSourceCore<int> core;
        int result;

        public ValueTask<int> Start(int value)
        {
            core.Reset();
            result = value;
            return new(this, core.Version);
        }

        public void Complete() => core.SetResult(result);

        public int GetResult(short token) => core.GetResult(token);

        public ValueTaskSourceStatus GetStatus(short token) => core.GetStatus(token);

        public void OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags
        ) => core.OnCompleted(continuation, state, token, flags);
    }
}
