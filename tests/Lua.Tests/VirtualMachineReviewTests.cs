using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class VirtualMachineReviewTests(LuaStringMode stringMode)
{
    LuaState CreateState()
    {
        var state = LuaState.Create(
            stringMode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        return state;
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task GenericForFitsReservedFrameCapacity(bool callable)
    {
        using var state = CreateState();
        var source = callable
            ? """
                local iterator=setmetatable({}, {__call=function(self,state,key)
                    if key == nil then return 1,5 end
                end})
                local sum=0; for key,value in iterator do sum=sum+value end; return sum
                """
            : "local sum=0; for key,value in ipairs({5}) do sum=sum+value end; return sum";
        var closure = state.Load(source, "generic-for-capacity");
        var prefix = state.Stack.GetBuffer().Length - closure.Proto.MaxStackSize;
        for (var i = 0; i < prefix; i++)
            state.Push(LuaValue.Nil);
        var result = await state.ExecuteAsync(closure);
        Assert.That(result[0].Read<double>(), Is.EqualTo(5));
        Assert.That(state.Stack.Count, Is.EqualTo(prefix));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task GenericForPadsMissingFixedArgumentsBeforeVarargs(bool callable)
    {
        using var state = CreateState();
        var parameters = callable ? "self,state,key,extra,..." : "state,key,extra,...";
        var setup = callable ? "iterator=setmetatable({}, {__call=iterator})" : "";
        var result = await state.DoStringAsync(
            $$"""
            local calls=0
            local function iterator({{parameters}})
                assert(state == 42 and extra == nil and select('#', ...) == 0)
                calls=calls+1
                if key == nil then return 1,5 end
            end
            {{setup}}
            local sum=0
            for key,value in iterator,42 do sum=sum+value end
            assert(sum == 5)
            return calls
            """
        );
        Assert.That(result[0].Read<double>(), Is.EqualTo(2));
        Assert.That(state.Stack.Count, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OpenCallsKeepMoreThan65535Arguments(bool tailCall)
    {
        using var state = CreateState();
        var call = tailCall
            ? "local function f() return select('#', table.unpack(t)) end; return f()"
            : "local n = select('#', table.unpack(t)); return n";
        var result = await state.DoStringAsync("local t={}; for i=1,70000 do t[i]=i end; " + call);
        Assert.That(result[0].Read<double>(), Is.EqualTo(70000));
    }

    [TestCase("__lt", "<")]
    [TestCase("__le", "<=")]
    [TestCase("__eq", "==")]
    public async Task ComparisonMetamethodsPrepareVarargs(string method, string op)
    {
        using var state = CreateState();
        var operands = await state.DoStringAsync(
            $$"""
            local a,b = {},{}
            local mt = { {{method}} = function(first, ...)
                local second = ...
                assert(first == a and second == b and select('#', ...) == 1)
                return true
            end }
            setmetatable(a, mt); setmetatable(b, mt)
            assert(a {{op}} b)
            return a,b
            """
        );
        state.Push(12345);
        var result = method switch
        {
            "__eq" => await state.EqualsAsync(operands[0], operands[1]),
            "__lt" => await state.LessThanAsync(operands[0], operands[1]),
            _ => await state.LessThanOrEqualsAsync(operands[0], operands[1]),
        };
        Assert.That(result, Is.True);
        Assert.That(state.Stack.Count, Is.EqualTo(1));
        Assert.That(state.Stack.Pop().Read<double>(), Is.EqualTo(12345));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LessEqualFallbackNegatesAsynchronousNativeResult(bool hooks)
    {
        using var state = CreateState();
        state.Environment["nativeLt"] = new LuaFunction(
            async (context, cancellationToken) =>
            {
                var a = context.GetArgument<LuaTable>(0)["x"].Read<double>();
                var b = context.GetArgument<LuaTable>(1)["x"].Read<double>();
                await Task.Yield();
                return context.Return(a < b);
            }
        );
        await state.DoStringAsync(
            """
            local mt={__lt=nativeLt}
            a=setmetatable({x=1}, mt)
            b=setmetatable({x=2}, mt)
            """
        );
        if (hooks)
            await state.DoStringAsync("debug.sethook(function() end, 'cr')");
        await state.DoStringAsync("assert(a <= b); assert(not (b <= a)); assert(a <= a)");
        Assert.That(
            await state.LessThanOrEqualsAsync(state.Environment["a"], state.Environment["b"]),
            Is.True
        );
        Assert.That(
            await state.LessThanOrEqualsAsync(state.Environment["b"], state.Environment["a"]),
            Is.False
        );
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [Test]
    public async Task VariadicIndexNewIndexAndConcatReceiveTheirOperands()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            local t=setmetatable({}, {
                __index=function(first, ...) local key=...; return key..select('#', ...) end,
                __newindex=function(first, ...) local key,value=...; rawset(first,key,value) end
            })
            assert(t.missing == 'missing1')
            t.value=42
            assert(t.value == 42)
            local mt={__concat=function(first, ...)
                local second=...
                return first.text..second.text..select('#', ...)
            end}
            local a=setmetatable({text='a'}, mt)
            local b=setmetatable({text='b'}, mt)
            assert(a..b == 'ab1')
            """
        );
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CompletedNativeComparisonObservesFailure(bool canceled)
    {
        using var state = CreateState();
        state.Environment["nativeLt"] = new LuaFunction(
            (context, cancellationToken) =>
                canceled
                    ? ValueTask.FromCanceled<int>(new CancellationToken(true))
                    : ValueTask.FromException<int>(
                        new InvalidOperationException("comparison failure")
                    )
        );
        const string source = """
            local mt={__lt=nativeLt}
            local a,b=setmetatable({},mt),setmetatable({},mt)
            return a < b
            """;
        if (canceled)
            Assert.ThrowsAsync<LuaCanceledException>(async () => await state.DoStringAsync(source));
        else
            Assert.ThrowsAsync<LuaRuntimeException>(async () => await state.DoStringAsync(source));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task IndexApiPreparesVarargsAndInvokesMetamethodOnce(bool hooks)
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            gets,sets=0,0
            t=setmetatable({}, {
                __index=function(first, ...)
                    gets=gets+1; local key=...; return key..select('#', ...)
                end,
                __newindex=function(first, ...)
                    sets=sets+1; local key,value=...; rawset(first,key,value)
                end
            })
            """
        );
        if (hooks)
            await state.DoStringAsync("debug.sethook(function() end, 'cr')");
        var table = state.Environment["t"];
        Assert.That(
            (await state.GetTableAsync(table, "missing")).Read<string>(),
            Is.EqualTo("missing1")
        );
        Assert.That(state.Environment["gets"].Read<double>(), Is.EqualTo(1));
        await state.SetTableAsync(table, "value", 42);
        Assert.That(state.Environment["sets"].Read<double>(), Is.EqualTo(1));
        Assert.That((await state.GetTableAsync(table, "value")).Read<double>(), Is.EqualTo(42));
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task IndexApiRestoresStackAfterMetamethodError(bool hooks)
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            t=setmetatable({}, {
                __index=function(...) error('index failure') end,
                __newindex=function(...) error('newindex failure') end
            })
            """
        );
        if (hooks)
            await state.DoStringAsync("debug.sethook(function() end, 'cr')");
        var table = state.Environment["t"];
        Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.GetTableAsync(table, "missing")
        );
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
        Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.SetTableAsync(table, "missing", 42)
        );
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
        await state.DoStringAsync("assert(1+2 == 3)");
    }

    [Test]
    public async Task HookedIndexApiInvokesFixedParameterMetamethodsOnce()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            gets,sets=0,0
            t=setmetatable({}, {
                __index=function(t,k) gets=gets+1; return k end,
                __newindex=function(t,k,v) sets=sets+1; rawset(t,k,v) end
            })
            debug.sethook(function() end, 'cr')
            """
        );
        state.Push(12345);
        var table = state.Environment["t"];
        Assert.That(
            (await state.GetTableAsync(table, "missing")).Read<string>(),
            Is.EqualTo("missing")
        );
        await state.SetTableAsync(table, "value", 42);
        Assert.That(state.Environment["gets"].Read<double>(), Is.EqualTo(1));
        Assert.That(state.Environment["sets"].Read<double>(), Is.EqualTo(1));
        Assert.That(state.Stack.Count, Is.EqualTo(1));
        Assert.That(state.Stack.Pop().Read<double>(), Is.EqualTo(12345));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }
}
