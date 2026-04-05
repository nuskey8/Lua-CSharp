using Lua.IO;
using Lua.Standard;

namespace Lua.Tests;

public sealed class IoBufferingTests : IDisposable
{
    readonly string testDirectory = Path.Combine(Path.GetTempPath(), $"LuaIoBufferingTests_{Guid.NewGuid()}");

    public IoBufferingTests()
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
    public async Task LineBufferedWrite_IsNotVisibleUntilNewline()
    {
        using var state = LuaState.Create();
        state.Platform = state.Platform with { FileSystem = new FileSystem(testDirectory) };
        state.OpenStandardLibraries();

        var result = await state.DoStringAsync(
            """
            local writer = assert(io.open("buffer.txt", "a"))
            local reader = assert(io.open("buffer.txt", "r"))
            assert(writer:setvbuf("line"))
            assert(writer:write("x"))
            reader:seek("set")
            local before = reader:read("*all")
            assert(writer:write("a\n"))
            reader:seek("set")
            local after = reader:read("*all")
            writer:close()
            reader:close()
            return before, after
            """);

        Assert.That(result, Has.Length.EqualTo(2));
        Assert.That(result[0], Is.EqualTo(new LuaValue("")));
        Assert.That(result[1], Is.EqualTo(new LuaValue("xa\n")));
    }
}