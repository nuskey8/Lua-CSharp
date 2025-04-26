using BenchmarkDotNet.Attributes;
using Lua;
using Lua.CodeAnalysis.Compilation;
using Lua.CodeAnalysis.Syntax;
using Lua.Runtime;
using Lua.Standard;

[Config(typeof(BenchmarkConfig))]
public class InterpreterSteps
{
    string sourceText = default!;
    LuaState state = default!;
    SyntaxToken[] tokens = [];
    LuaSyntaxTree ast = default!;
    Chunk chunk = default!;
    LuaValue[] results = new LuaValue[1];

    [GlobalSetup]
    public void GlobalSetup()
    {
        var filePath = FileHelper.GetAbsolutePath("n-body.lua");
        sourceText = File.ReadAllText(filePath);

        var lexer = new Lexer
        {
            Source = sourceText.AsMemory()
        };

        var buffer = new List<SyntaxToken>();
        while (lexer.MoveNext())
        {
            buffer.Add(lexer.Current);
        }

        tokens = buffer.ToArray();

        var parser = new Parser();
        foreach (var token in tokens)
        {
            parser.Add(token);
        }

        ast = parser.Parse();
        chunk = LuaCompiler.Default.Compile(ast);
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
        LuaState.Create();
    }

    [Benchmark]
    public void Lexer()
    {
        var lexer = new Lexer
        {
            Source = sourceText.AsMemory()
        };

        while (lexer.MoveNext()) { }
    }

    [Benchmark]
    public LuaSyntaxTree Parser()
    {
        var parser = new Parser();
        foreach (var token in tokens)
        {
            parser.Add(token);
        }

        return parser.Parse();
    }

    [Benchmark]
    public Chunk Compile()
    {
        return LuaCompiler.Default.Compile(ast);
    }

    [Benchmark]
    public async ValueTask RunAsync()
    {
        using (await state.RunAsync(chunk))
        {
        }
    }
}