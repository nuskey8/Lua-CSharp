using Lua.IO;
using Lua.Platforms;
using Lua.Standard;
using Lua.Tests.Helpers;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class DefaultIOStreamTests(LuaStringMode mode)
{
    LuaState Create(RecordingFileSystem files, FlushStream output)
    {
        var platform =
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default;
        var state = LuaState.Create(
            platform with
            {
                FileSystem = files,
                StandardIO = new TestStandardIO(mode) { Output = output },
            }
        );
        state.OpenStandardLibraries();
        return state;
    }

    [Test, Combinatorial]
    public async Task InvalidArgumentsDoNotOpenFilesOrChangeTheDefault(
        [Values("input", "output")] string operation,
        [Values("false", "{}", "function() end", "(coroutine.running())")] string argument
    )
    {
        var files = new RecordingFileSystem();
        using var output = new FlushStream();
        using var state = Create(files, output);
        await state.DoStringAsync(
            $$"""
            local select=io.{{operation}}
            local original=select()
            local ok,e=pcall(select,{{argument}})
            assert(not ok and type(e)=='string')
            assert(select()==original)
            """
        );
        Assert.That(files.Paths, Is.Empty);
        Assert.That(output.FlushCount, Is.Zero);
    }

    [TestCase("input")]
    [TestCase("output")]
    public async Task ClosedHandlesDoNotChangeTheDefault(string operation)
    {
        var files = new RecordingFileSystem();
        using var output = new FlushStream();
        using var state = Create(files, output);
        var file = new FileHandle(ILuaStream.CreateFromString(""));
        await file.Close(default);
        state.Environment["closed"] = new LuaValue(file);
        await state.DoStringAsync(
            $$"""
            local select=io.{{operation}}
            local original=select()
            local ok,e=pcall(select,closed)
            assert(not ok and e:find('closed file',1,true))
            assert(select()==original)
            """
        );
        Assert.That(files.Paths, Is.Empty);
        Assert.That(output.FlushCount, Is.Zero);
    }

    [TestCase("input")]
    [TestCase("output")]
    public async Task NumericNamesAndOpenHandlesRemainSupported(string operation)
    {
        var files = new RecordingFileSystem();
        using var output = new FlushStream();
        using var state = Create(files, output);
        await state.DoStringAsync(
            $$"""
            local select=io.{{operation}}
            local file=select(123)
            assert(io.type(file)=='file' and select()==file)
            assert(select(file)==file)
            """
        );
        Assert.That(files.Paths, Is.EqualTo(new[] { "123" }));
    }

    sealed class RecordingFileSystem : NotImplementedExceptionFileSystemBase
    {
        public List<string> Paths { get; } = [];

        public override ValueTask<ILuaStream> Open(
            string path,
            LuaFileOpenMode mode,
            CancellationToken token
        )
        {
            Paths.Add(path);
            return new(new LuaStream(mode, new MemoryStream()));
        }
    }

    sealed class FlushStream : ILuaStream
    {
        public bool IsOpen => true;
        public LuaFileOpenMode Mode => LuaFileOpenMode.Write;
        public int FlushCount { get; private set; }

        public ValueTask FlushAsync(CancellationToken cancellationToken)
        {
            FlushCount++;
            return default;
        }

        public void Dispose() { }
    }
}
