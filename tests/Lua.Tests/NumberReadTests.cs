using System.Text;
using Lua.IO;

namespace Lua.Tests;

[TestFixture("string")]
[TestFixture("memory")]
[TestFixture("utf8")]
[TestFixture("bytes")]
public class NumberReadTests(string backend)
{
    [TestCase("1e2e3x", 100, "e3x")]
    [TestCase("1e2.3", 100, ".3")]
    [TestCase("-0x10z", -16, "z")]
    [TestCase("+0X1.Ap+2z", 6.5, "z")]
    [TestCase("0x1p2p3z", 4, "p3z")]
    [TestCase("0x1p2az", 4, "az")]
    [TestCase("0x1p2+3", 4, "+3")]
    [TestCase("  .5e-1x", 0.05, "x")]
    public async Task NumericReadsPreserveTheFirstNonNumericCharacter(
        string input,
        double expected,
        string remaining
    )
    {
        using var stream = CreateStream(input);
        Assert.That(await stream.ReadNumberAsync(default), Is.EqualTo(expected));
        Assert.That(await stream.ReadAllAsync(default), Is.EqualTo(remaining));
    }

    ILuaStream CreateStream(string text) =>
        backend switch
        {
            "string" => ILuaStream.CreateFromString(text),
            "memory" => ILuaStream.CreateFromMemory(text.AsMemory()),
            _ => ILuaStream.CreateFromStream(
                new ShortReadStream(Encoding.ASCII.GetBytes(text)),
                LuaFileOpenMode.Read,
                backend == "bytes" ? LuaStringMode.Bytes : LuaStringMode.Utf16
            ),
        };

    sealed class ShortReadStream(byte[] contents) : MemoryStream(contents)
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            base.Read(buffer, offset, Math.Min(count, 1));
    }
}
