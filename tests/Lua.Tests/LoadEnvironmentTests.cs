using System.Text;
using Lua.IO;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

public sealed class LoadEnvironmentTests : IDisposable
{
    readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"LuaLoadEnvironmentTests_{Guid.NewGuid()}"
    );

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

        var result = await state.DoStringAsync(
            "local f = assert(load('return _ENV', nil, 't', nil)); return f()"
        );

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(LuaValue.Nil));
    }

    [Test]
    public async Task LoadFile_WithExplicitNilEnvironment_ReturnsNilEnvironment()
    {
        await File.WriteAllBytesAsync(
            Path.Combine(testDirectory, "env.lua"),
            Encoding.UTF8.GetBytes("return _ENV")
        );

        using var state = LuaState.Create();
        state.Platform = state.Platform with { FileSystem = new FileSystem(testDirectory) };
        state.OpenStandardLibraries();

        var result = await state.DoStringAsync(
            "local f = assert(loadfile('env.lua', 't', nil)); return f()"
        );

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(LuaValue.Nil));
    }

    [TestCase("")]
    [TestCase(",nil")]
    [TestCase(",false")]
    [TestCase(",{value=42}")]
    public async Task LoadFileInitializesMultipleUpvalues(string environmentArgument)
    {
        using var state = LuaState.Create();
        state.Platform = state.Platform with { FileSystem = new FileSystem(testDirectory) };
        state.OpenStandardLibraries();
        var original = (
            await state.DoStringAsync(
                "local first,second=11,22; return function() return first,second end"
            )
        )[0]
            .Read<LuaClosure>();
        await File.WriteAllBytesAsync(
            Path.Combine(testDirectory, "captured.luac"),
            original.Proto.ToBytecode()
        );
        var results = await state.DoStringAsync(
            "local f=assert(loadfile('captured.luac','b'" + environmentArgument + ")); return f()"
        );
        if (environmentArgument == ",{value=42}")
            Assert.That(results[0].Read<LuaTable>()["value"], Is.EqualTo(new LuaValue(42)));
        else
            Assert.That(
                results[0],
                Is.EqualTo(environmentArgument == ",false" ? new LuaValue(false) : LuaValue.Nil)
            );
        Assert.That(results[1], Is.EqualTo(LuaValue.Nil));
    }
}
