using Lua.IO;
using Lua.Platforms;
using Lua.Standard;
using Lua.Tests.Helpers;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class DefaultIOStreamTests(LuaStringMode mode)
{
    LuaState Create(ILuaFileSystem files, FlushStream output)
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

    [Test]
    public async Task CloseDistinguishesAnOmittedArgumentFromNil()
    {
        using var output = new FlushStream();
        using var state = Create(new RecordingFileSystem(), output);
        using var stream = new LuaStream(LuaFileOpenMode.Write, new MemoryStream(), mode);
        state.Environment["file"] = new LuaValue(new FileHandle(stream));
        await state.DoStringAsync(
            """
            local original=io.output()
            io.output(file)
            local ok,e=pcall(io.close,nil)
            assert(not ok and type(e)=='string')
            assert(io.type(file)=='file' and io.output()==file)
            assert(io.close())
            assert(io.type(file)=='closed file')
            io.output(original)
            """
        );
    }

    [TestCase("file:lines()")]
    [TestCase("io.lines()")]
    [TestCase("io.lines(nil,'*l')")]
    public async Task LineIteratorFactoriesRejectClosedFiles(string expression)
    {
        using var output = new FlushStream();
        using var state = Create(new RecordingFileSystem(), output);
        using var stream = ILuaStream.CreateFromString("line\n");
        state.Environment["file"] = new LuaValue(new FileHandle(stream));
        await state.DoStringAsync(
            $$"""
            local original=io.input()
            io.input(file)
            local iterator=file:lines()
            assert(iterator()=='line')
            file:close()
            local ok,e=pcall(function() return {{expression}} end)
            assert(not ok and e:find('closed file',1,true))
            io.input(original)
            """
        );
    }

    [Test]
    public async Task AccessDeniedIsReturnedByOpenButRaisedByLines()
    {
        var files = new DeniedFileSystem();
        using var output = new FlushStream();
        using var state = Create(files, output);
        var result = await state.DoStringAsync(
            """
            local file,message,code=io.open('denied','r')
            local ok,e=pcall(io.lines,'denied')
            return file,message,code,ok,type(e)
            """
        );
        Assert.That(
            result,
            Is.EqualTo(
                new LuaValue[]
                {
                    LuaValue.Nil,
                    "access denied",
                    files.Error.HResult,
                    false,
                    "string",
                }
            )
        );
    }

    sealed class DeniedFileSystem : NotImplementedExceptionFileSystemBase
    {
        public UnauthorizedAccessException Error { get; } = new("access denied");

        public override ValueTask<ILuaStream> Open(
            string path,
            LuaFileOpenMode mode,
            CancellationToken token
        ) => throw Error;
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
