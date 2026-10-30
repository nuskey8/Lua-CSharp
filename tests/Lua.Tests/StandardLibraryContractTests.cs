using Lua.IO;
using Lua.Platforms;
using Lua.Standard;
using Lua.Tests.Helpers;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class StandardLibraryContractTests(LuaStringMode stringMode)
{
    LuaState CreateState()
    {
        var state = LuaState.Create(
            stringMode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        return state;
    }

    [Test]
    public async Task RawSetReturnsTheOriginalTable()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            local t=setmetatable({}, {__newindex=function() error('must not run') end})
            assert(rawset(t,'x',7)==t and t.x==7)
            assert(select('#',rawset(t,'x',nil))==1 and t.x==nil)
            """
        );
    }

    [Test]
    public async Task AssertCoercesNumericMessagesButRejectsTables()
    {
        using var state = CreateState();
        var result = await state.DoStringAsync("return pcall(assert,false,42)");
        Assert.That(result, Is.EqualTo(new LuaValue[] { false, "42" }));
        await state.DoStringAsync(
            """
            local ok,e=pcall(assert,false,{})
            assert(not ok and e:find('string expected',1,true))
            assert(select('#',assert(true,42,nil))==3)
            """
        );
    }

    [Test]
    public async Task PrintUsesOneSnapshotOfGlobalTostring()
    {
        using var state = CreateState();
        var output = new List<string>();
        var io = new TestStandardIO(stringMode)
        {
            Output = new BufferedOutputStream(text => output.Add(text.ToString())),
        };
        state.Platform = state.Platform with { StandardIO = io };
        await state.DoStringAsync(
            """
            local hits=0
            tostring=function(value)
                hits=hits+1
                _G.tostring=function() error('replacement must not run') end
                return value*2,'extra'
            end
            print(1,2)
            assert(hits==2)
            """
        );
        Assert.That(output, Is.EqualTo(new[] { "2\t4\n" }));
    }

    [TestCase("nil")]
    [TestCase("{}")]
    public async Task PrintRejectsInvalidTostringResults(string result)
    {
        using var state = CreateState();
        await state.DoStringAsync(
            "tostring=function() return "
                + result
                + " end;local ok,e=pcall(print,1);assert(not ok and e:find('must return a string',1,true))"
        );
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PrintResolvesTostringThroughTheGlobalMetatable(bool asynchronous)
    {
        using var state = CreateState();
        var suspension = new ManualSuspension();
        state.Environment["pause"] = new LuaFunction(
            async (context, _) =>
            {
                if (asynchronous)
                    await suspension;
                return context.Return();
            }
        );
        var output = new List<string>();
        state.Platform = state.Platform with
        {
            StandardIO = new TestStandardIO(stringMode)
            {
                Output = new BufferedOutputStream(value => output.Add(value.ToString())),
            },
        };
        var operation = state
            .DoStringAsync(
                """
                local calls=0
                _G.tostring=nil
                setmetatable(_G,{__index=function(t,key)
                    pause()
                    assert(key=='tostring')
                    calls=calls+1
                    return function(value) return value*2 end
                end})
                print(1,2)
                assert(calls==1)
                """
            )
            .AsTask();
        if (asynchronous)
            suspension.Resume();
        await operation;
        Assert.That(output, Is.EqualTo(new[] { "2\t4\n" }));
    }

    [Test]
    public async Task FileLinesRetainsEveryReadFormat()
    {
        using var state = CreateState();
        using var stream = ILuaStream.CreateFromString("12 abc\n34 def\n");
        state.Environment["file"] = new LuaValue(new FileHandle(stream));
        var result = await state.DoStringAsync(
            "local it=file:lines('*n','*l');local a,b=it();local c,d=it();return a,b,c,d,it(),io.type(file)"
        );
        Assert.That(
            result,
            Is.EqualTo(new LuaValue[] { 12, " abc", 34, " def", LuaValue.Nil, "file" })
        );
    }

    [Test]
    public async Task LuaIntegerArgumentsTruncateWithoutChangingTypedCSharpArguments()
    {
        using var state = CreateState();
        using var stream = ILuaStream.CreateFromString("abc");
        state.Environment["file"] = new LuaValue(new FileHandle(stream));
        state.Environment["typed"] = new LuaFunction(
            (context, _) => new(context.Return(context.GetArgument<int>(0)))
        );
        await state.DoStringAsync(
            """
            assert(file:read(1.9)=='a')
            local value=42
            local function f() return value end
            local name,upvalue=debug.getupvalue(f,1.9)
            assert(name=='value' and upvalue==42)
            assert(debug.setupvalue(f,1.9,43)=='value' and f()==43)
            local ok,e=pcall(function() error('fail',0.9) end)
            assert(not ok and e=='fail')
            assert(not pcall(typed,1.9))
            """
        );
    }
}
