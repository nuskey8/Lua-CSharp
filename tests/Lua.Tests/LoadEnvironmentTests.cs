using System.Text;
using Lua.IO;
using Lua.Standard;

namespace Lua.Tests;

public sealed class LoadEnvironmentTests : IDisposable
{
    readonly string testDirectory = Path.Combine(Path.GetTempPath(), $"LuaLoadEnvironmentTests_{Guid.NewGuid()}");

    public LoadEnvironmentTests()
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
    public async Task Load_WithExplicitNilEnvironment_ReturnsNilEnvironment()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();

        var result = await state.DoStringAsync("local f = assert(load('return _ENV', nil, 't', nil)); return f()");

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(LuaValue.Nil));
    }

    [Test]
    public async Task LoadFile_WithExplicitNilEnvironment_ReturnsNilEnvironment()
    {
        await File.WriteAllBytesAsync(
            Path.Combine(testDirectory, "env.lua"),
            Encoding.UTF8.GetBytes("return _ENV"));

        using var state = LuaState.Create();
        state.Platform = state.Platform with { FileSystem = new FileSystem(testDirectory) };
        state.OpenStandardLibraries();

        var result = await state.DoStringAsync("local f = assert(loadfile('env.lua', 't', nil)); return f()");

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(LuaValue.Nil));
    }
}