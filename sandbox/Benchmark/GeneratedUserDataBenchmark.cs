using BenchmarkDotNet.Attributes;
using Lua;
using Lua.Runtime;

[Config(typeof(ComparisonConfig))]
public class GeneratedUserDataBenchmark
{
    [Params("field", "method", "comparison", "write")]
    public string Case { get; set; } = "field";

    [Params(false, true)]
    public bool OverrideMetatable { get; set; }

    LuaState state = default!;
    LuaClosure closure = default!;

    [GlobalSetup]
    public void Setup()
    {
        state = LuaState.Create();
        var first = new GeneratedBenchmarkObject { Value = 7 };
        var second = new GeneratedBenchmarkObject { Value = 8 };
        if (OverrideMetatable)
        {
            ILuaUserData left = first;
            ILuaUserData right = second;
            left.Metatable = left.Metatable;
            right.Metatable = right.Metatable;
        }
        state.Environment["first"] = first;
        state.Environment["second"] = second;
        var (source, expected) = Case switch
        {
            "field" => ("local v=first;local n=0;for i=1,1000 do n=n+v.Value end;return n", 7000),
            "method" => ("local v=first;local n=0;for i=1,1000 do n=n+v:Add(1) end;return n", 8000),
            "comparison" => (
                "local a,b=first,second;local n=0;for i=1,1000 do if a<b then n=n+1 end end;return n",
                1000
            ),
            "write" => ("local v=first;for i=1,1000 do v.Value=i end;return v.Value", 1000),
            _ => throw new ArgumentOutOfRangeException(nameof(Case)),
        };
        closure = state.Load(source, Case);
        if (Run() != expected)
            throw new InvalidOperationException($"Unexpected result for {Case}");
    }

    [Benchmark(OperationsPerInvoke = 1000)]
    public int Run()
    {
        state.RunAsync(closure).GetAwaiter().GetResult();
        return state.Stack.Pop().Read<int>();
    }

    [GlobalCleanup]
    public void Cleanup() => state.Dispose();
}

[LuaObject]
public partial class GeneratedBenchmarkObject
{
    [LuaMember]
    public int Value { get; set; }

    [LuaMember]
    public int Add(int value) => Value + value;

    [LuaMetamethod(LuaObjectMetamethod.Lt)]
    public static bool Less(GeneratedBenchmarkObject left, GeneratedBenchmarkObject right) =>
        left.Value < right.Value;
}
