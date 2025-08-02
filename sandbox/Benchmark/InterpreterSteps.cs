using BenchmarkDotNet.Attributes;
using Lua;
using Lua.Runtime;
using Lua.Standard;

[Config(typeof(BenchmarkConfig))]
public class InterpreterSteps
{
    string sourceText = default!;
    LuaState state = default!;
    LuaClosure closure = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var filePath = FileHelper.GetAbsolutePath("n-body.lua");
        sourceText = File.ReadAllText(filePath);
        state = LuaState.Create();
        state.OpenStandardLibraries();
        closure = state.Load(sourceText, sourceText);
    }

    [IterationSetup]
    public void Setup()
    {
        state = default!;
        GC.Collect();

        state = LuaState.Create();
        state.OpenStandardLibraries();
    }

    [Benchmark]
    public void CreateState()
    {
        LuaGlobalState.Create();
    }


    [Benchmark]
    public LuaClosure Compile()
    {
        return state.Load(sourceText, sourceText);
    }

    [Benchmark]
    public async ValueTask RunAsync()
    {
        await state.Call(closure, []);
    }
}