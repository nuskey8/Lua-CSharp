using BenchmarkDotNet.Attributes;
using Lua;
using Lua.Runtime;
using Lua.Standard;

[Config(typeof(BenchmarkConfig))]
public class InterpreterSteps
{
    string sourceText = default!;
    LuaGlobalState globalState = default!;
    LuaClosure closure = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var filePath = FileHelper.GetAbsolutePath("n-body.lua");
        sourceText = File.ReadAllText(filePath);
        globalState = LuaGlobalState.Create();
        globalState.OpenStandardLibraries();
        closure = globalState.Load(sourceText, sourceText);
    }

    [IterationSetup]
    public void Setup()
    {
        globalState = default!;
        GC.Collect();

        globalState = LuaGlobalState.Create();
        globalState.OpenStandardLibraries();
    }

    [Benchmark]
    public void CreateState()
    {
        LuaGlobalState.Create();
    }


    [Benchmark]
    public LuaClosure Compile()
    {
        return globalState.Load(sourceText, sourceText);
    }

    [Benchmark]
    public async ValueTask RunAsync()
    {
        await globalState.RootAccess.Call(closure, []);
    }
}