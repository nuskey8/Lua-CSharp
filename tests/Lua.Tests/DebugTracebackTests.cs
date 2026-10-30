using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class DebugTracebackTests(LuaStringMode mode)
{
    [TestCase("failed")]
    [TestCase("completed")]
    [TestCase("unstarted")]
    public async Task CoroutineTracebacksPreserveTheSuppliedMessage(string status)
    {
        using var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        state.Environment["status"] = status;
        await state.DoStringAsync(
            """
            local co=coroutine.create(function()
                if status=='failed' then error('failure',0) end
                return 42
            end)
            if status~='unstarted' then
                local ok,e=coroutine.resume(co)
                assert(ok==(status=='completed'))
                assert(debug.traceback(co,e):sub(1,#tostring(e))==tostring(e))
            end
            local message='message ế\0done'
            local prefix=message..'\nstack traceback:'
            assert(debug.traceback(co,message):sub(1,#prefix)==prefix)
            assert(debug.traceback(co,message,0):sub(1,#prefix)==prefix)
            assert(debug.traceback(co,message,1000)==prefix)
            assert(debug.traceback(co,42,0):sub(1,3)=='42\n')
            assert(debug.traceback(co,nil,0):sub(1,16)=='stack traceback:')
            local object={}
            assert(debug.traceback(co,object)==object)
            """,
            "@traceback-workload.lua"
        );
    }
}
