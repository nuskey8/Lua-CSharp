using System.Text;
using Lua.IO;

namespace Lua.Tests;

public sealed class LoadFileBomTests : IDisposable
{
    readonly string testDirectory = Path.Combine(Path.GetTempPath(), $"LuaLoadFileBomTests_{Guid.NewGuid()}");

    public LoadFileBomTests()
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

    async Task<LuaValue[]> ExecuteFileAsync(string fileName, byte[] bytes)
    {
        await File.WriteAllBytesAsync(Path.Combine(testDirectory, fileName), bytes);

        using var state = LuaState.Create();
        state.Platform = state.Platform with { FileSystem = new FileSystem(testDirectory) };

        var closure = await state.LoadFileAsync(fileName, "bt", null, CancellationToken.None);
        return await state.ExecuteAsync(closure);
    }

    [Test]
    public async Task LoadFile_Utf8BomBeforeComment_IsIgnored()
    {
        var bytes = Encoding.UTF8.GetBytes("\uFEFF# some comment\nreturn 234");

        var result = await ExecuteFileAsync("bom-comment.lua", bytes);

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(234)));
    }

    [Test]
    public async Task LoadFile_Utf8BomBeforeCode_IsIgnored()
    {
        var bytes = Encoding.UTF8.GetBytes("\uFEFFreturn 239");

        var result = await ExecuteFileAsync("bom-code.lua", bytes);

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(239)));
    }

    [Test]
    public async Task LoadFile_Utf8BomOnly_ProducesEmptyChunk()
    {
        var bytes = Encoding.UTF8.GetBytes("\uFEFF");

        var result = await ExecuteFileAsync("bom-only.lua", bytes);

        Assert.That(result, Is.Empty);
    }
}