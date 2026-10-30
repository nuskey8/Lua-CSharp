using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class DebugTracebackTests(LuaStringMode mode)
{
    [TestCase(LuaNumberFormat.DotNet)]
    [TestCase(LuaNumberFormat.Lua)]
    public async Task TracebackHandlesMessagesAndNegativeLevelsBeforeFormatting(
        LuaNumberFormat format
    )
    {
        var platform =
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default;
        using var state = LuaState.Create(platform with { NumberFormat = format });
        state.OpenStandardLibraries();
        await state.DoStringAsync(
            """
            local co=coroutine.create(function() end)
            for _,object in ipairs({{},false,function() end}) do
                assert(debug.traceback(object,'invalid')==object)
                assert(debug.traceback(co,object,'invalid')==object)
            end
            assert(debug.traceback(co,co,'invalid')==co)
            local message=tostring(1/3)..'\nstack traceback:'
            assert(debug.traceback(1/3,-1)==message)
            assert(debug.traceback(co,1/3,-1)==message)
            assert(debug.traceback(nil,-1)=='stack traceback:')
            assert(debug.traceback(nil,2147483647)=='stack traceback:')
            """
        );
    }

    [Test]
    public async Task CurrentThreadDefaultsAndExplicitLevelsSkipTheSameFrames()
    {
        using var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        await state.DoStringAsync(
            """
            local function leaf()
                local co=coroutine.running()
                local implicit,explicit=debug.traceback(),debug.traceback(co)
                assert(implicit==explicit)
                local traces={}
                for level=0,5 do
                    local a,b=debug.traceback(nil,level),debug.traceback(co,nil,level)
                    assert(a==b)
                    traces[level]=a
                end
                assert(traces[0]:find("in function 'debug.traceback'",1,true))
                assert(traces[1]:find("'leaf'",1,true) and not traces[2]:find("'leaf'",1,true))
                assert(traces[2]:find("'parent'",1,true) and not traces[3]:find("'parent'",1,true))
                assert(traces[3]:find("'outer'",1,true) and not traces[4]:find("'outer'",1,true))
                assert(traces[5]=='stack traceback:')
                return true
            end
            local function parent() local result=leaf();return result end
            local function outer() local result=parent();return result end
            assert(outer())
            """,
            "@traceback-levels.lua"
        );
    }

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
