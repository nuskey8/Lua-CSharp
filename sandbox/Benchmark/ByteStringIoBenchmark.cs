using BenchmarkDotNet.Attributes;
using Lua.IO;

[Config(typeof(BenchmarkConfig))]
public class ByteStringIoBenchmark
{
    [Params(false, true)]
    public bool ByteStrings { get; set; }

    [Params(1, 4096)]
    public int Size { get; set; }

    MemoryStream input = default!;
    MemoryStream output = default!;
    LuaStream reader = default!;
    LuaStream writer = default!;
    string text = default!;

    [GlobalSetup]
    public void Setup()
    {
        text = new string('a', Size);
        input = new MemoryStream(Enumerable.Repeat((byte)'a', Size).ToArray());
        output = new MemoryStream(Size);
        reader = new LuaStream(LuaFileOpenMode.Read, input, ByteStrings);
        writer = new LuaStream(LuaFileOpenMode.Write, output, ByteStrings);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        reader.Dispose();
        writer.Dispose();
    }

    [Benchmark]
    public string? Read()
    {
        reader.Seek(SeekOrigin.Begin, 0);
        return reader.ReadAsync(Size, default).GetAwaiter().GetResult();
    }

    [Benchmark]
    public void Write()
    {
        output.Position = 0;
        writer.WriteAsync(text.AsMemory(), default).GetAwaiter().GetResult();
    }
}
