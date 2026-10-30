using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class LoadReaderTests(LuaStringMode stringMode)
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
    public async Task ReaderLoadsPiecesWithAnEnvironmentAndChunkName()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            local pieces={'return value+',42}
            local index=0
            local f=assert(load(function()
                index=index+1
                return pieces[index],'ignored'
            end,'reader-name','t',{value=1}))
            assert(index==3 and f()==43)
            assert(debug.getinfo(f).source=='reader-name')
            """
        );
    }

    [TestCase("nil")]
    [TestCase("''")]
    public async Task ReaderStopsAtTheFirstEmptyPiece(string ending)
    {
        using var state = CreateState();
        await state.DoStringAsync(
            "local calls=0;local f=assert(load(function() calls=calls+1;return "
                + ending
                + " end));assert(calls==1 and select('#',f())==0)"
        );
    }

    [Test]
    public async Task ReaderErrorsAreReturnedAndDoNotLeakFrames()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            local payload={}
            local f,e=load(function() error(payload) end)
            assert(f==nil and e==payload)
            f,e=load(function() return true end)
            assert(f==nil and e:find('reader function must return a string',1,true))
            assert(not pcall(load,{}))
            """
        );
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase("return 42", "b", "text", false)]
    [TestCase("return 42", "b", "text", true)]
    [TestCase("\u001bLua", "t", "binary", false)]
    [TestCase("\u001bLua", "t", "binary", true)]
    [TestCase("", "b", "text", false)]
    [TestCase(null, "b", "text", false)]
    public async Task ReaderChecksModeBeforeRequestingAnotherPiece(
        string? piece,
        string mode,
        string kind,
        bool asynchronous
    )
    {
        using var state = CreateState();
        var suspension = new ManualSuspension();
        var calls = 0;
        state.Environment["reader"] = new LuaFunction(
            async (context, _) =>
            {
                if (++calls > 1)
                    throw new InvalidOperationException("reader must not be called again");
                if (asynchronous)
                    await suspension;
                return context.Return(piece == null ? LuaValue.Nil : piece);
            }
        );
        state.Environment["mode"] = mode;
        var operation = state.DoStringAsync("return load(reader,nil,mode)").AsTask();
        if (asynchronous)
            suspension.Resume();
        var result = await operation;
        Assert.That(result, Has.Length.EqualTo(2));
        Assert.That(result[0], Is.EqualTo(LuaValue.Nil));
        Assert.That(result[1].Read<string>(), Does.Contain($"attempt to load a {kind} chunk"));
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
        Assert.That(state.NonYieldableCallCount, Is.Zero);
    }

    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(1, true)]
    public async Task ReaderCannotYieldAndRestoresYieldability(int piecesBeforeYield, bool nested)
    {
        using var state = CreateState();
        state.Environment["piecesBeforeYield"] = piecesBeforeYield;
        state.Environment["nested"] = nested;
        await state.DoStringAsync(
            """
            local co=coroutine.create(function()
                local calls=0
                local f,e=load(function()
                    calls=calls+1
                    if calls<=piecesBeforeYield then return 'return 42' end
                    if nested then
                        local inner,message=load(function() coroutine.yield('inner reader') end)
                        assert(inner==nil and message:find('attempt to yield across a C-call boundary',1,true))
                    end
                    coroutine.yield('reader')
                end)
                assert(f==nil and e:find('attempt to yield across a C-call boundary',1,true))
                coroutine.yield('after load')
                return 'done'
            end)
            local ok,value=coroutine.resume(co)
            assert(ok and value=='after load' and coroutine.status(co)=='suspended')
            ok,value=coroutine.resume(co)
            assert(ok and value=='done' and coroutine.status(co)=='dead')
            """
        );
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [Test]
    public async Task ReaderCanSuspendAndCancellationIsNotConvertedToALoadError()
    {
        using var state = CreateState();
        var suspension = new ManualSuspension();
        var calls = 0;
        state.Environment["reader"] = new LuaFunction(
            async (context, _) =>
            {
                if (calls++ > 0)
                    return context.Return();
                await suspension;
                return context.Return("return 42");
            }
        );
        var operation = state.DoStringAsync("return assert(load(reader))()").AsTask();
        suspension.Resume();
        Assert.That(await operation, Is.EqualTo(new LuaValue[] { 42 }));
        Assert.That(state.NonYieldableCallCount, Is.Zero);
        using var source = new CancellationTokenSource();
        state.Environment["reader"] = new LuaFunction(
            (context, token) =>
            {
                source.Cancel();
                token.ThrowIfCancellationRequested();
                return new(context.Return());
            }
        );
        Assert.ThrowsAsync<LuaCanceledException>(async () =>
            await state.DoStringAsync("return load(reader)", cancellationToken: source.Token)
        );
        Assert.That(state.NonYieldableCallCount, Is.Zero);
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DumpedFunctionsLoadDirectlyAndFromOneCharacterPieces(bool reader)
    {
        using var state = CreateState();
        state.Environment["readerMode"] = reader;
        await state.DoStringAsync(
            """
            local captured=99
            local function original(x,...)
                local function nested(y) return x+y end
                return nested(2),captured,'ế\0\255',...
            end
            local binary=string.dump(original)
            local source=binary
            if readerMode then
                local i=0
                source=function() i=i+1;return binary:sub(i,i) end
            end
            local loaded=assert(load(source,'dump-name','b',41))
            local a,b,text,c=loaded(40,43)
            assert(a==42 and b==41 and text=='ế\0\255' and c==43)
            assert(captured==99)
            local ok,message=pcall(string.dump,print)
            assert(not ok and message:find('unable to dump',1,true))
            local f,message=load(binary,nil,'t')
            assert(not f and message:find('attempt to load a binary chunk',1,true))
            f,message=load('return 42',nil,'b')
            assert(not f and message:find('attempt to load a text chunk',1,true))
            """
        );
    }

    [Test]
    public async Task NativeLua52BytecodeUsesTheSameFunctionHeader()
    {
        // Lua 5.2.4: string.dump(function(a, ...) return a + 2, ... end), 64-bit size_t.
        var bytes = Convert.FromBase64String(
            "G0x1YVIAAQQIBAgAGZMNChoKAQAAAAEAAAABAQMEAAAATQBAAKYAAABfAAAAHwCAAAEAAAADAAAAAAAAAEAAAAAAAAAAAAgAAAAAAAAAQG5hdGl2ZQAEAAAAAQAAAAEAAAABAAAAAQAAAAEAAAACAAAAAAAAAGEAAAAAAAQAAAAAAAAA"
        );
        using var state = CreateState();
        var loaded = new LuaClosure(state, Prototype.FromBytecode(bytes, "native", stringMode));
        state.Stack.Push(40);
        state.Stack.Push(43);
        var count = await state.RunAsync(loaded, 2);
        Assert.That(count, Is.EqualTo(2));
        Assert.That(state.Stack.Pop(), Is.EqualTo(new LuaValue(43)));
        Assert.That(state.Stack.Pop(), Is.EqualTo(new LuaValue(42)));

        var dump = (
            await state.DoStringAsync("return string.dump(function(a,...) return a+2,... end)")
        )[0]
            .Read<string>();
        Assert.That(dump[26], Is.EqualTo((char)1)); // parameter count
        Assert.That(dump[27], Is.EqualTo((char)1)); // varargs
        Assert.That(dump[28], Is.GreaterThanOrEqualTo((char)2)); // register window
    }
}
