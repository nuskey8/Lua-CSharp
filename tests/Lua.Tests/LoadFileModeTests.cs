using System.Text;
using Lua.IO;

namespace Lua.Tests;

public sealed class LoadFileModeTests : IDisposable
{
    readonly string testDirectory = Path.Combine(Path.GetTempPath(), $"LuaLoadFileModeTests_{Guid.NewGuid()}");

    public LoadFileModeTests()
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

    LuaState CreateState()
    {
        var state = LuaState.Create();
        state.Platform = state.Platform with { FileSystem = new FileSystem(testDirectory) };
        return state;
    }

    [Test]
    public void LoadFile_BinaryModeRejectsTextChunk()
    {
        File.WriteAllBytes(Path.Combine(testDirectory, "text.lua"), Encoding.UTF8.GetBytes("return 10"));

        using var state = CreateState();

        var exception = Assert.ThrowsAsync<Exception>(async () => await state.LoadFileAsync("text.lua", "b", null, CancellationToken.None));

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.Message, Does.Contain("a text chunk"));
    }

    [Test]
    public void LoadFile_TextModeRejectsBinaryChunk()
    {
        File.WriteAllBytes(Path.Combine(testDirectory, "binary.luac"), [0x1B, (byte)' ', (byte)'r', (byte)'e', (byte)'t', (byte)'u', (byte)'r', (byte)'n']);

        using var state = CreateState();

        var exception = Assert.ThrowsAsync<Exception>(async () => await state.LoadFileAsync("binary.luac", "t", null, CancellationToken.None));

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.Message, Does.Contain("a binary chunk"));
    }
}