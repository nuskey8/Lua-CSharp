using Lua.Standard;

namespace Lua.Tests;

public class LightUserDataMetatableTests
{
    [Test]
    public async Task MetatableIsSharedByLightUserDataAndCoroutines()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["host"] = LuaValue.FromObject(new object());
        await state.DoStringAsync(
            """
            local value=1
            local a=debug.upvalueid(function() return value end,1)
            local mt={__index={answer=42},__metatable='protected',
                __newindex=function(_,k,v) value=v end,
                __add=function() return 12 end,__unm=function() return -12 end,
                __len=function() return 7 end,__concat=function() return 'joined' end,
                __lt=function() return true end,__le=function() return true end,
                __eq=function() error('light userdata equality must use identity') end,
                __call=function(_,v) return v end,__tostring=function() return 'token' end}
            assert(debug.setmetatable(a,mt)==a)
            assert(debug.getmetatable(host)==mt and getmetatable(host)=='protected')
            assert(a~=host and a==a)
            assert(host.answer==42 and host+1==12 and -host==-12 and #host==7)
            assert(host..'x'=='joined' and host<1 and host<=1 and host(9)==9)
            assert(tostring(host)=='token')
            host.answer=17
            assert(value==17)
            local co=coroutine.create(function()
                assert(debug.getmetatable(a)==mt)
                debug.setmetatable(host,{__index={answer=99}})
            end)
            assert(coroutine.resume(co))
            assert(a.answer==99)
            debug.setmetatable(a,nil)
            assert(debug.getmetatable(host)==nil and getmetatable(a)==nil)
            assert(not pcall(function() return host.answer end))
            """
        );
    }

    [Test]
    public async Task IndependentStatesHaveIndependentLightUserDataMetatables()
    {
        var token = LuaValue.FromObject(new object());
        using var first = LuaState.Create();
        using var second = LuaState.Create();
        foreach (var state in new[] { first, second })
        {
            state.OpenStandardLibraries();
            state.Environment["token"] = token;
        }
        await first.DoStringAsync("debug.setmetatable(token,{__index={answer=42}})");
        await second.DoStringAsync("assert(debug.getmetatable(token)==nil)");
        await first.DoStringAsync("assert(token.answer==42)");
    }
}
