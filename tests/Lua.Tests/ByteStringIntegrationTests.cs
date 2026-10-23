using System.Text;
using Lua.IO;
using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

public sealed class ByteStringIntegrationTests : IDisposable
{
    readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"LuaBytes_\u1EBF_{Guid.NewGuid()}"
    );

    public ByteStringIntegrationTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, true);

    LuaState CreateState()
    {
        var state = LuaState.Create(
            LuaPlatform.DefaultByteStrings with
            {
                FileSystem = new FileSystem(directory, true),
            }
        );
        state.OpenStandardLibraries();
        return state;
    }

    [Test]
    public void ExistingConstructorSignaturesRemainAvailable()
    {
        Assert.That(typeof(FileSystem).GetConstructor([typeof(string)]), Is.Not.Null);
        Assert.That(
            typeof(LuaStream).GetConstructor([typeof(LuaFileOpenMode), typeof(Stream)]),
            Is.Not.Null
        );
        Assert.That(typeof(ConsoleStandardIO).GetConstructor(Type.EmptyTypes), Is.Not.Null);
    }

    [Test]
    public async Task TemporaryFilesPreserveRawBytesAndPositions()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            local bytes = string.char(239, 187, 191, 0, 128, 255)
            local f = assert(io.tmpfile())
            f:write(bytes)
            assert(f:seek("end") == #bytes)
            f:seek("set", 0)
            assert(f:read(1):byte() == 239)
            assert(f:seek() == 1)
            f:seek("set", 0)
            assert(f:read("*a") == bytes)
            f:close()
            """
        );
    }

    [TestCase("local f,e=loadfile(path); return f~=nil,e")]
    [TestCase("return pcall(io.input,path)")]
    [TestCase("return xpcall(function() dofile(path) end,function(e) return e end)")]
    public async Task FileErrorsContainTheOriginalUtf8Path(string script)
    {
        using var state = CreateState();
        var path = LuaByteString.FromString(Path.Combine(directory, "missing_\u1EBF", "file.lua"));
        state.Environment["path"] = path;
        var result = await state.DoStringAsync(script, LuaByteString.FromString("caller_\u1EBF"));

        Assert.That(result[0], Is.EqualTo(new LuaValue(false)));
        var message = result[1].Read<string>();
        Assert.That(message.All(c => c <= 255), Is.True);
        Assert.That(message, Does.Contain(path));
    }

    [Test]
    public async Task LuaErrorsAndSyntaxDiagnosticsAreNotReencoded()
    {
        using var state = CreateState();
        var text = LuaByteString.FromString("Lua error \u1EBF");
        state.Environment["message"] = text;
        state.Environment["chunk"] = LuaByteString.FromString("chunk_\u1EBF");
        var result = await state.DoStringAsync(
            """
            local _,p = pcall(error,message,0)
            local _,x = xpcall(function() error(message,0) end,function(e) return e end)
            local _,s = load("return )",chunk)
            local payload = {}
            local ok,value = xpcall(function() error(payload) end,function(e) return e end)
            assert(not ok and value == payload)
            return p,x,s
            """
        );
        Assert.That(result[0].Read<string>(), Is.EqualTo(text));
        Assert.That(result[1].Read<string>(), Is.EqualTo(text));
        Assert.That(
            result[2].Read<string>(),
            Does.Contain(state.Environment["chunk"].Read<string>())
        );
    }

    [TestCase("read('*a')")]
    [TestCase("write('x')")]
    [TestCase("flush()")]
    public async Task FileHandleErrorsAreUtf8Bytes(string operation)
    {
        using var state = CreateState();
        state.Environment["file"] = LuaValue.FromUserData(new FileHandle(new FailingStream()));
        var result = await state.DoStringAsync($"return file:{operation}");
        Assert.That(result[0], Is.EqualTo(LuaValue.Nil));
        Assert.That(
            result[1].Read<string>(),
            Is.EqualTo(LuaByteString.FromString(FailingStream.Error))
        );
    }

    [TestCase(1)]
    [TestCase(8193)]
    public async Task BinaryPacketsRoundTripAcrossBufferBoundaries(int chunkSize)
    {
        var bytes = Enumerable.Range(0, 20000).Select(i => (byte)i).ToArray();
        using var memory = new MemoryStream();
        using var stream = new LuaStream(LuaFileOpenMode.WriteUpdate, memory, true);
        await stream.WriteAsync(LuaByteString.FromBytes(bytes).AsMemory(), default);
        Assert.That(memory.ToArray(), Is.EqualTo(bytes));
        stream.Seek(SeekOrigin.Begin, 0);
        var result = new StringBuilder();
        string? part;
        while ((part = await stream.ReadAsync(chunkSize, default)) != null)
            result.Append(part);
        Assert.That(LuaByteString.ToBytes(result.ToString()), Is.EqualTo(bytes));
        Assert.That(stream.Seek(SeekOrigin.Current, 0), Is.EqualTo(bytes.Length));
    }

    [Test]
    public async Task RawLinesPreserveCarriageReturnsIncludingAtBufferEdges()
    {
        var first = new string('a', 1023) + "\r";
        var bytes = LuaByteString.ToBytes(first + "\nnext\rpart\nlast\r");
        using var stream = new LuaStream(LuaFileOpenMode.Read, new MemoryStream(bytes), true);
        Assert.That(await stream.ReadLineAsync(false, default), Is.EqualTo(first));
        Assert.That(await stream.ReadLineAsync(true, default), Is.EqualTo("next\rpart\n"));
        Assert.That(await stream.ReadLineAsync(false, default), Is.EqualTo("last\r"));
        Assert.That(await stream.ReadLineAsync(false, default), Is.Null);
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task HostModuleLoadersReceiveDotNetNamesAndReturnTextOrBytes(
        bool byteStrings,
        bool bytes
    )
    {
        var loader = new MemoryModuleLoader(bytes);
        using var state = LuaState.Create(
            byteStrings ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.ModuleLoader = loader;
        state.OpenStandardLibraries();
        state.Environment["name"] = byteStrings
            ? LuaByteString.FromString(MemoryModuleLoader.Name)
            : MemoryModuleLoader.Name;
        var result = await state.DoStringAsync("local a=require(name); return a.text,a.size");
        Assert.That(
            result[0].Read<string>(),
            Is.EqualTo(byteStrings ? LuaByteString.FromString("\u1EBF") : "\u1EBF")
        );
        Assert.That(result[1], Is.EqualTo(new LuaValue(byteStrings ? 3 : 1)));
    }

    sealed class MemoryModuleLoader(bool bytes) : ILuaModuleLoader
    {
        public const string Name = "memory_\u1EBF";

        public bool Exists(string moduleName) => moduleName == Name;

        public async ValueTask<LuaModule> LoadAsync(
            string moduleName,
            CancellationToken ct = default
        )
        {
            Assert.That(moduleName, Is.EqualTo(Name));
            await Task.Yield();
            const string source = "return {text='\u1EBF',size=#'\u1EBF'}";
            return bytes
                ? new LuaModule(moduleName, Encoding.UTF8.GetBytes(source))
                : new LuaModule(moduleName, source);
        }
    }

    sealed class FailingStream : ILuaStream
    {
        public const string Error = "I/O failure \u1EBF";
        public bool IsOpen => true;
        public LuaFileOpenMode Mode => LuaFileOpenMode.ReadUpdate;

        public ValueTask<string> ReadAllAsync(CancellationToken ct) => throw new IOException(Error);

        public ValueTask WriteAsync(ReadOnlyMemory<char> value, CancellationToken ct) =>
            throw new IOException(Error);

        public ValueTask FlushAsync(CancellationToken ct) => throw new IOException(Error);

        public void Dispose() { }
    }
}
