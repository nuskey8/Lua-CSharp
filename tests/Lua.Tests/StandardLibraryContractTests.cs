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

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(10000)]
    public async Task ErrorCoercesNumbersOnlyAtPositiveLevels(int level)
    {
        using var state = CreateState();
        state.Environment["level"] = level;
        await state.DoStringAsync(
            """
            local function fail() error(42,level) end
            local function check(value)
                if level>0 then
                    assert(type(value)=='string' and value:sub(-2)=='42')
                else
                    assert(type(value)=='number' and value==42)
                end
                return value
            end
            local ok,value=pcall(fail)
            assert(not ok)
            check(value)
            ok,value=xpcall(fail,check)
            assert(not ok)
            check(value)
            local object={}
            ok,value=pcall(error,object,level)
            assert(not ok and value==object)
            """
        );
    }

    [Test]
    public async Task IPairsIteratorTruncatesItsControlAndReturnsOneNilAtExhaustion()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            local it,t=ipairs({'first','second'})
            local key,value=it(t,'0.9')
            assert(key==1 and value=='first')
            assert(select('#',it(t,1))==2)
            assert(select('#',it(t,2))==1 and it(t,2)==nil)
            """
        );
    }

    [TestCase("file:lines()")]
    [TestCase("file:lines('*L')")]
    [TestCase("file:lines(100)")]
    [TestCase("io.lines()")]
    [TestCase("io.lines(nil, '*n', '*l')")]
    [TestCase("file:lines('*n', '*l')")]
    public async Task LineIteratorsReturnNoValuesWhenTheFirstReadFails(string expression)
    {
        using var state = CreateState();
        using var stream = ILuaStream.CreateFromString("12 word\n");
        state.Environment["file"] = new LuaValue(new FileHandle(stream));
        await state.DoStringAsync(
            $$"""
            io.input(file)
            local it={{expression}}
            assert(it()~=nil)
            assert(select('#',it())==0)
            assert(select('#',it())==0)
            assert(io.type(file)=='file')
            assert(select('#',file:read())==1 and file:read()==nil)
            """
        );
    }

    [Test]
    public async Task LineIteratorsKeepPartialResultsWhenALaterReadFails()
    {
        using var state = CreateState();
        using var stream = ILuaStream.CreateFromString("12");
        state.Environment["file"] = new LuaValue(new FileHandle(stream));
        await state.DoStringAsync(
            """
            local it=file:lines('*n','*n')
            local values=table.pack(it())
            assert(values.n==2 and values[1]==12 and values[2]==nil)
            assert(select('#',it())==0)
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

    [TestCase("lookup")]
    [TestCase("function")]
    [TestCase("metamethod")]
    public async Task PrintCannotYieldAndRestoresYieldability(string callback)
    {
        using var state = CreateState();
        state.Environment["callback"] = callback;
        await state.DoStringAsync(
            """
            local value=42
            if callback=='lookup' then
                tostring=nil
                setmetatable(_G,{__index=function(t,key)
                    assert(key=='tostring')
                    coroutine.yield('lookup')
                end})
            elseif callback=='function' then
                tostring=function() coroutine.yield('tostring') end
            else
                value=setmetatable({},{__tostring=function() coroutine.yield('metamethod') end})
            end
            local co=coroutine.create(function()
                local ok,e=pcall(print,value)
                assert(not ok and e:find('attempt to yield across a C-call boundary',1,true))
                coroutine.yield('after print')
                return 42
            end)
            local ok,value=coroutine.resume(co)
            assert(ok and value=='after print')
            ok,value=coroutine.resume(co)
            assert(ok and value==42 and coroutine.status(co)=='dead')
            """
        );
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase("tostring=function() cancel() end;print(42)")]
    [TestCase("setmetatable(package.loaded,{__index=function() cancel() end});require('test')")]
    public async Task NonYieldableLibraryCallsRestoreStateAfterCancellation(string script)
    {
        using var state = CreateState();
        using var source = new CancellationTokenSource();
        state.Environment["cancel"] = new LuaFunction(
            (context, token) =>
            {
                source.Cancel();
                token.ThrowIfCancellationRequested();
                return new(context.Return());
            }
        );
        Assert.ThrowsAsync<LuaCanceledException>(async () =>
            await state.DoStringAsync(script, cancellationToken: source.Token)
        );
        Assert.That(state.NonYieldableCallCount, Is.Zero);
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
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
