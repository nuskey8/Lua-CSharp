using Lua;
using MoonSharp.Interpreter;

public class BenchmarkCore : IDisposable
{
    public NLua.Lua NLuaState => nLuaState;
    public Script MoonSharpState => moonSharpState;
    public LuaGlobalState LuaGlobalCSharpState => luaGlobalCSharpState;
    public string FilePath => filePath;
    public string SourceText => sourceText;

    NLua.Lua nLuaState = default!;
    Script moonSharpState = default!;
    LuaGlobalState luaGlobalCSharpState = default!;
    string filePath = default!;
    string sourceText = default!;

    public void Setup(string fileName)
    {
        // moonsharp
        moonSharpState = new();
        Script.WarmUp();

        // NLua
        nLuaState = new();

        // Lua-CSharp
        luaGlobalCSharpState = LuaGlobalState.Create();

        filePath = FileHelper.GetAbsolutePath(fileName);
        sourceText = File.ReadAllText(filePath);
    }

    public void Dispose()
    {
        nLuaState.Dispose();
    }
}