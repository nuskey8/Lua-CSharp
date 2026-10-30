using Lua.IO;
using Lua.Platforms;
using Lua.Standard;
using Lua.Tests.Helpers;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class TmpFileTests(LuaStringMode mode)
{
    LuaState CreateState(ILuaFileSystem? fileSystem = null)
    {
        var platform =
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default;
        if (fileSystem is not null)
            platform = platform with { FileSystem = fileSystem };
        var state = LuaState.Create(platform);
        state.OpenStandardLibraries();
        return state;
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task CreationFailuresReturnTheIoErrorTuple(bool denied, bool asynchronous)
    {
        Exception error = denied
            ? new UnauthorizedAccessException("denied ế")
            : new IOException("failed ế");
        using var state = CreateState(new FailingFileSystem(error, asynchronous));
        var result = await state.DoStringAsync("return pcall(io.tmpfile)");
        Assert.That(
            result,
            Is.EqualTo(
                new LuaValue[]
                {
                    true,
                    LuaValue.Nil,
                    state.Platform.EncodeText(error.Message),
                    error.HResult,
                }
            )
        );
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CreationCancellationStillEscapesPcall(bool asynchronous)
    {
        using var state = CreateState(
            new FailingFileSystem(new OperationCanceledException(), asynchronous)
        );
        Assert.ThrowsAsync<LuaCanceledException>(async () =>
            await state.DoStringAsync("return pcall(io.tmpfile)")
        );
    }

    [Test]
    public async Task SuccessfulCreationReturnsOneUsableFile()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            local values=table.pack(io.tmpfile())
            assert(values.n==1 and io.type(values[1])=='file')
            local file=values[1]
            assert(file:write('temporary')==file)
            assert(file:seek('set',0)==0 and file:read('*a')=='temporary')
            assert(file:close() and io.type(file)=='closed file')
            """
        );
    }

    sealed class FailingFileSystem(Exception error, bool asynchronous)
        : NotImplementedExceptionFileSystemBase
    {
        public override async ValueTask<ILuaStream> OpenTempFileStream(
            CancellationToken cancellationToken
        )
        {
            if (asynchronous)
                await Task.Yield();
            throw error;
        }
    }
}
