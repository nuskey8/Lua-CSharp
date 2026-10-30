using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class StrippedBytecodeTests(LuaStringMode mode)
{
    // Lua 5.2.4 luac -s, 64-bit size_t:
    // local value=42;return function()if value==42 then error('stripped failure') end;return value end
    static readonly byte[] Bytecode = Convert.FromBase64String(
        "G0x1YVIAAQQIBAgAGZMNChoKAAAAAAAAAAAAAQIEAAAAAQAAAGUAAABfAAABHwCAAAEAAAADAAAAAAAARUABAAAAAQAAAAEAAAAAAAIJAAAABQAAABgAQAAXgACABkDAAEGAAAAdQAABBQAAAB8AAAEfAIAAAwAAAAMAAAAAAABFQAQGAAAAAAAAAGVycm9yAAQRAAAAAAAAAHN0cmlwcGVkIGZhaWx1cmUAAAAAAAIAAAABAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAA=="
    );

    LuaState CreateState()
    {
        var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        state.Environment["binary"] = LuaByteString.FromBytes(Bytecode);
        return state;
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MissingDebugMetadataPreservesErrorsAndUnknownSource(bool redump)
    {
        using var state = CreateState();
        state.Environment["redump"] = redump;
        var result = await state.DoStringAsync(
            """
            local f=assert(load(binary,'@supplied','b'))()
            if redump then
                f=assert(load(string.dump(f)))
                debug.setupvalue(f,1,42)
                debug.setupvalue(f,2,_G)
            end
            local info=debug.getinfo(f,'S')
            assert(info.source=='=?' and info.short_src=='?')
            local ok,message=pcall(f)
            assert(not ok and message=='stripped failure')
            return xpcall(f,function(e)
                local trace=debug.traceback(e)
                assert(not trace:find('Trace back error',1,true))
                assert(trace:find('?:',1,true))
                return e
            end)
            """
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { false, "stripped failure" }));
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [Test]
    public async Task AnEmptySourceNameRemainsDifferentFromMissingSource()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            local f=assert(load('return 42',''))
            f=assert(load(string.dump(f)))
            local info=debug.getinfo(f,'S')
            assert(info.source=='' and info.short_src=='[string ""]' and f()==42)
            """
        );
    }
}
