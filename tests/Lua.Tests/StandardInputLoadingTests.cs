using System.Text;
using Lua.IO;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

public class StandardInputLoadingTests
{
    sealed class StandardIO(ILuaStream input) : ILuaStandardIO
    {
        public ILuaStream Input => input;
        public ILuaStream Output => input;
        public ILuaStream Error => input;
    }

    static LuaState CreateState(ILuaStream input, LuaStringMode mode)
    {
        var state = LuaState.Create();
        state.Platform = state.Platform with
        {
            StandardIO = new StandardIO(input),
            StringMode = mode,
        };
        state.OpenBasicLibrary();
        state.OpenDebugLibrary();
        state.OpenStringLibrary();
        return state;
    }

    [TestCase("return assert(loadfile())()", LuaStringMode.Utf16)]
    [TestCase("return assert(loadfile(nil))()", LuaStringMode.Bytes)]
    [TestCase("return dofile()", LuaStringMode.Utf16)]
    [TestCase("return dofile(nil)", LuaStringMode.Bytes)]
    public async Task OmittedAndNilFilenamesReadStandardInput(string source, LuaStringMode mode)
    {
        using var bytes = new MemoryStream(
            Encoding.UTF8.GetBytes("\uFEFF#!/lua\nreturn 42,nil,'é'")
        );
        using var input = new LuaStream(LuaFileOpenMode.Read, bytes, mode);
        using var state = CreateState(new StandardIOStream(input), mode);
        var result = await state.DoStringAsync(source);
        Assert.That(
            result,
            Is.EqualTo(new LuaValue[] { 42, LuaValue.Nil, state.Platform.EncodeText("é") })
        );
        Assert.That(bytes.CanRead, Is.True);
    }

    [TestCase("{answer=42}", "return answer", 42)]
    [TestCase("nil", "\uFEFF#!/lua\nreturn _ENV", null)]
    public async Task TextProvidersSupportEnvironmentsAndStdinChunkNames(
        string environment,
        string source,
        int? expected
    )
    {
        using var input = ILuaStream.CreateFromString(source);
        using var state = CreateState(input, LuaStringMode.Utf16);
        var result = await state.DoStringAsync(
            "local f=assert(loadfile(nil,'t',"
                + environment
                + ")); assert(debug.getinfo(f,'S').source=='=stdin'); return f()"
        );
        Assert.That(
            result,
            Is.EqualTo(new[] { expected.HasValue ? new LuaValue(expected.Value) : LuaValue.Nil })
        );
        Assert.That(input.IsOpen, Is.True);
    }

    [TestCase(false, LuaStringMode.Utf16)]
    [TestCase(true, LuaStringMode.Bytes)]
    public async Task BinaryStandardInputPreservesBytesAndHonorsMode(
        bool binaryAllowed,
        LuaStringMode mode
    )
    {
        using var compiler = LuaState.Create();
        var chunk = compiler.Load("return 'binary',42", "@binary").Proto.ToBytecode();
        using var bytes = new MemoryStream(chunk);
        using var input = new LuaStream(LuaFileOpenMode.Read, bytes, mode);
        using var state = CreateState(new StandardIOStream(input), mode);
        var result = await state.DoStringAsync(
            binaryAllowed
                ? "return assert(loadfile(nil,'b'))()"
                : "local f,err=loadfile(nil,'t'); assert(f==nil and err:find('a binary chunk',1,true)); return true"
        );
        Assert.That(
            result,
            Is.EqualTo(binaryAllowed ? new LuaValue[] { "binary", 42 } : new LuaValue[] { true })
        );
        Assert.That(bytes.CanRead, Is.True);
    }

    [TestCase("local f,err=loadfile(); assert(f==nil and err:find('stdin',1,true))")]
    [TestCase("local ok,err=pcall(dofile); assert(not ok and err:find('stdin',1,true))")]
    public async Task SyntaxErrorsPreserveInputOwnership(string source)
    {
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes("#!/lua\nlocal ="));
        using var input = new LuaStream(LuaFileOpenMode.Read, bytes);
        using var state = CreateState(input, LuaStringMode.Utf16);
        await state.DoStringAsync(source);
        Assert.That(bytes.CanRead, Is.True);
    }

    [TestCase("")]
    [TestCase("#!/lua")]
    public async Task EmptyStandardInputCompilesAnEmptyChunk(string source)
    {
        using var input = ILuaStream.CreateFromString(source);
        using var state = CreateState(input, LuaStringMode.Utf16);
        var result = await state.DoStringAsync("return assert(loadfile())()");
        Assert.That(result, Is.Empty);
        Assert.That(input.IsOpen, Is.True);
    }

    [Test]
    public async Task StandardInputIsIndependentOfIoLibrarySelections()
    {
        using var input = ILuaStream.CreateFromString("return 42");
        using var other = ILuaStream.CreateFromString("return 99");
        using var state = CreateState(input, LuaStringMode.Utf16);
        state.OpenIOLibrary();
        state.Environment["other"] = LuaValue.FromUserData(
            new FileHandle(other, state.GlobalState.FileHandleMetatable)
        );
        var result = await state.DoStringAsync(
            """
            io.input(other)
            io.stdin=nil
            return assert(loadfile())()
            """
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { 42 }));
    }

    [Test]
    public async Task BinaryModeRejectsTextProvidersWithoutClosingThem()
    {
        using var input = ILuaStream.CreateFromString("return 42");
        using var state = CreateState(input, LuaStringMode.Utf16);
        await state.DoStringAsync(
            """
            local f,err=loadfile(nil,'b')
            assert(f==nil and err:find('a text chunk',1,true))
            """
        );
        Assert.That(input.IsOpen, Is.True);
    }
}
