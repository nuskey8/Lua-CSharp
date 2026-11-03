public sealed record ComparisonCase(string Name, string ScriptPath)
{
    public string Source => BenchmarkScripts.Read(ScriptPath);
}

public static class ComparisonWorkloads
{
    // ASCII input keeps the UTF-16, byte-string and native UTF-8 engines comparable.
    // Every invocation rebuilds its mutable Lua data and returns one numeric checksum.
    public static readonly ComparisonCase[] Cases =
    [
        Execution("numeric-loop"),
        Execution("calls-varargs"),
        Execution("fib"),
        Execution("metamethods"),
        Execution("table-access"),
        Execution("table-sort"),
        Execution("map"),
        Execution("math"),
        Execution("strings"),
        Execution("patterns"),
        Execution("coroutines"),
        Execution("host-calls"),
        Execution("mandelbrot"),
        Execution("nbody"),
    ];

    public static readonly ComparisonCase[] CompilationCases =
    [
        Find("numeric-loop"),
        Find("nbody"),
        Compilation("control-flow"),
        Compilation("identifiers"),
        Compilation("plain-strings"),
        Compilation("escaped-strings"),
    ];

    static ComparisonCase Execution(string name) => new(name, $"Comparison/{name}.lua");

    static ComparisonCase Compilation(string name) => new(name, $"Compilation/{name}.lua");

    public static ComparisonCase Find(string name) => Cases.Single(c => c.Name == name);

    public static ComparisonCase FindCompilation(string name) =>
        CompilationCases.Single(c => c.Name == name);
}
