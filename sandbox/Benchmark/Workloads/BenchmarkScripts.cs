public static class BenchmarkScripts
{
    // Stable names also work when these sources are linked into the NativeAOT host.
    public static string Read(string path)
    {
        var resourceName = "Benchmark.Scripts." + path.Replace('/', '.');
        using var stream =
            typeof(BenchmarkScripts).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing benchmark script: {path}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
