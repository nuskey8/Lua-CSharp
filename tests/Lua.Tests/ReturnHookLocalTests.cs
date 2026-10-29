using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class ReturnHookLocalTests(LuaStringMode mode)
{
    LuaState CreateState()
    {
        var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        state.Stack.PushRange([101, 102]);
        return state;
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReturnHookInspectsAndUpdatesTheCalleesLocals(bool asynchronous)
    {
        using var state = CreateState();
        state.Environment["pause"] = new LuaFunction(
            async (context, _) =>
            {
                if (asynchronous)
                    await Task.Yield();
                return context.Return();
            }
        );
        var result = await state.DoStringAsync(
            """
            local seen
            local function target(a,...)
                local marker=123
                return a,marker
            end
            debug.sethook(function(event)
                if event=='return' and debug.getinfo(2,'f').func==target then
                    pause()
                    local name,a=debug.getlocal(2,1)
                    local localName,marker=debug.getlocal(2,2)
                    local varName,before=debug.getlocal(2,-1)
                    assert(debug.setlocal(2,-1,777)==varName)
                    local _,after=debug.getlocal(2,-1)
                    seen={name,a,localName,marker,varName,before,after,event}
                end
            end,'r')
            local a,marker=target(42,99)
            debug.sethook()
            return a,marker,table.unpack(seen)
            """
        );
        Assert.That(
            result,
            Is.EqualTo(
                new LuaValue[] { 42, 123, "a", 42, "marker", 123, "(*vararg)", 99, 777, "return" }
            )
        );
        Assert.That(state.GetStackValues().ToArray(), Is.EqualTo(new LuaValue[] { 101, 102 }));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReturnHookPreservesOpenResultsWhenItsStackGrows(bool asynchronous)
    {
        using var state = CreateState();
        var values = Enumerable.Range(1, 400).Select(i => new LuaValue(i)).ToArray();
        state.Environment["expand"] = new LuaFunction((context, _) => new(context.Return(values)));
        state.Environment["pause"] = new LuaFunction(
            async (context, _) =>
            {
                if (asynchronous)
                    await Task.Yield();
                return context.Return();
            }
        );
        var result = await state.DoStringAsync(
            """
            local calls=0
            local function target(...) return ... end
            debug.sethook(function(event)
                if event=='return' and debug.getinfo(2,'f').func==target then
                    local extra={expand()}
                    assert(extra[400]==400)
                    pause()
                    calls=calls+1
                end
            end,'r')
            local results={target(expand())}
            debug.sethook()
            assert(calls==1)
            return table.unpack(results)
            """
        );
        Assert.That(result, Is.EqualTo(values));
        Assert.That(state.GetStackValues().ToArray(), Is.EqualTo(new LuaValue[] { 101, 102 }));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [Test]
    public async Task HookEnabledInsideAFunctionRunsBeforeThatFunctionReturns()
    {
        using var state = CreateState();
        var result = await state.DoStringAsync(
            """
            local seen
            local target
            target=function(a)
                debug.sethook(function(event)
                    if event=='return' and debug.getinfo(2,'f').func==target then
                        local name,value=debug.getlocal(2,1)
                        seen={name,value}
                    end
                end,'r')
                return a
            end
            local value=target(42)
            debug.sethook()
            return value,seen and seen[1],seen and seen[2]
            """
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { 42, "a", 42 }));
    }
}
