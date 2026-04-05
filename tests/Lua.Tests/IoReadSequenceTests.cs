using Lua.IO;
using Lua.Standard;

namespace Lua.Tests;

public sealed class IoReadSequenceTests : IDisposable
{
    readonly string testDirectory = Path.Combine(Path.GetTempPath(), $"LuaIoReadSequenceTests_{Guid.NewGuid()}");

    public IoReadSequenceTests()
    {
        Directory.CreateDirectory(testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, true);
        }
    }

    [Test]
    public async Task IoRead_PreservesEarlierResultsWhenFinalFixedReadHitsEof()
    {
        await File.WriteAllTextAsync(
            Path.Combine(testDirectory, "sequence.txt"),
            """
             123.4	-56e-2  not a number
            second line
            third line

            and the rest of the file
            """);

        using var state = LuaState.Create();
        state.Platform = state.Platform with { FileSystem = new FileSystem(testDirectory) };
        state.OpenStandardLibraries();

        var result = await state.DoStringAsync(
            """
            io.input("sequence.txt")
            local _,a,b,c,d,e,h,__ = io.read(1, '*n', '*n', '*l', '*l', '*l', '*a', 10)
            assert(io.close(io.input()))
            return _, a, b, c, d, e, h, __
            """);

        Assert.That(result, Has.Length.EqualTo(8));
        Assert.Multiple(() =>
        {
            Assert.That(result[0], Is.EqualTo(new LuaValue(" ")));
            Assert.That(result[1], Is.EqualTo(new LuaValue(123.4)));
            Assert.That(result[2], Is.EqualTo(new LuaValue(-56e-2)));
            Assert.That(result[3], Is.EqualTo(new LuaValue("  not a number")));
            Assert.That(result[4], Is.EqualTo(new LuaValue("second line")));
            Assert.That(result[5], Is.EqualTo(new LuaValue("third line")));
            Assert.That(result[6].ToString(), Does.Contain("and the rest of the file"));
            Assert.That(result[7], Is.EqualTo(LuaValue.Nil));
        });
    }
}