using System.Text;
using Lua.IO;
using Lua.Standard;

namespace Lua.Tests;

public class IoReadFormatTests
{
    [TestCase("*bad", false)]
    [TestCase("*bad", true)]
    [TestCase("", false)]
    [TestCase("", true)]
    public async Task UnknownFormatRaisesLuaErrorWithoutReadingInput(
        string format,
        bool defaultInput
    )
    {
        using var stream = new LuaStream(
            LuaFileOpenMode.Read,
            new MemoryStream(Encoding.UTF8.GetBytes("abc"))
        );
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["file"] = new LuaValue(new FileHandle(stream));
        state.Environment["format"] = format;
        await state.DoStringAsync("io.input(file)");
        var reader = defaultInput ? "io.read(format)" : "file:read(format)";
        var result = await state.DoStringAsync(
            "local ok,err=pcall(function() return " + reader + " end); return ok,err,file:read(1)"
        );
        Assert.That(result[0], Is.EqualTo(new LuaValue(false)));
        Assert.That(result[1].Read<string>(), Does.Contain("invalid format"));
        Assert.That(result[2], Is.EqualTo(new LuaValue("a")));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UnknownFormatFromHostPreservesStackPrefix(bool defaultInput)
    {
        using var stream = new LuaStream(
            LuaFileOpenMode.Read,
            new MemoryStream(Encoding.UTF8.GetBytes("abc"))
        );
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var file = new LuaValue(new FileHandle(stream));
        state.Environment["file"] = file;
        await state.DoStringAsync("io.input(file)");
        var read = (
            await state.DoStringAsync(defaultInput ? "return io.read" : "return file.read")
        )[0]
            .Read<LuaFunction>();
        state.Push(42);
        if (!defaultInput)
            state.Push(file);
        state.Push("*bad");
        var exception = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.RunAsync(read, defaultInput ? 1 : 2)
        )!;
        Assert.That(exception.InnerException, Is.Null);
        Assert.That(exception.ErrorObject.Read<string>(), Does.Contain("invalid format"));
        Assert.That(state.Stack.Count, Is.EqualTo(1));
        Assert.That(state.Stack.Pop(), Is.EqualTo(new LuaValue(42)));
    }
}
