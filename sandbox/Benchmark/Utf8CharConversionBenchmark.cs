using System.Text;
using BenchmarkDotNet.Attributes;
using Lua;

[MemoryDiagnoser]
[ShortRunJob]
public class Utf8CharConversionBenchmark
{
    [Params("Ascii", "SparseUnicode", "Japanese", "Mixed", "InvalidUtf16")]
    public string Corpus { get; set; } = "Ascii";

    [Params(4096)]
    public int Length { get; set; }

    string source = default!;

    [GlobalSetup]
    public void Setup()
    {
        var unit = Corpus switch
        {
            "Ascii" => "hello world 1234 ",
            "SparseUnicode" => new string('a', Length - 1) + "ế",
            "Japanese" => "こんにちは世界",
            "Mixed" => "aế😀漢字 ",
            "InvalidUtf16" => "a\uD800b\uDC00c",
            _ => throw new ArgumentOutOfRangeException(nameof(Corpus)),
        };
        source = string.Concat(Enumerable.Repeat(unit, Length / unit.Length + 1))[..Length];
    }

    [Benchmark(Baseline = true)]
    public string ViaBytes() => LuaByteString.FromBytes(Encoding.UTF8.GetBytes(source));

    [Benchmark]
    public string DirectChars() => LuaByteString.EncodeUtf8(source);
}
