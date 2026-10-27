using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class VirtualMachineHelperTests(LuaStringMode stringMode)
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
    public async Task ChainedIndexMetamethodUsesCurrentReceiver(
        [Values] bool setter,
        [Values] bool api,
        [Values] bool hooks,
        [Values] bool nonTable,
        [Values] bool native
    )
    {
        using var state = CreateState();
        var method = setter ? "__newindex" : "__index";
        state.Environment["host"] = new LuaFunction(
            async (context, token) =>
            {
                await Task.Yield();
                Assert.That(context.GetArgument(0), Is.EqualTo(state.Environment["b"]));
                Assert.That(context.GetArgument(1), Is.EqualTo(new LuaValue("missing")));
                if (setter)
                    Assert.That(context.GetArgument(2), Is.EqualTo(new LuaValue(42)));
                state.Environment["calls"] = state.Environment["calls"].Read<double>() + 1;
                return context.Return(42);
            }
        );
        await state.DoStringAsync(
            $$"""
            calls=0
            b={{(nonTable ? "7" : "{}")}}
            local op={{(
                native
                    ? "host"
                    : "function(t,k,v) assert(t==b and k=='missing'); calls=calls+1; return 42 end"
            )}}
            debug.setmetatable(b,{ {{method}}=op })
            a=setmetatable({}, { {{method}}=b })
            """
        );
        if (hooks)
            await state.DoStringAsync("debug.sethook(function() end,'cr')");

        if (api)
        {
            state.Push(12345);
            if (setter)
                await state.SetTableAsync(state.Environment["a"], "missing", 42);
            else
                Assert.That(
                    await state.GetTableAsync(state.Environment["a"], "missing"),
                    Is.EqualTo(new LuaValue(42))
                );
            Assert.That(state.Stack.Count, Is.EqualTo(1));
            Assert.That(state.Stack.Pop(), Is.EqualTo(new LuaValue(12345)));
        }
        else
        {
            await state.DoStringAsync(setter ? "a.missing=42" : "assert(a.missing==42)");
        }
        Assert.That(state.Environment["calls"].Read<double>(), Is.EqualTo(1));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [Test]
    public async Task NewIndexDiscardsResultsWithoutGrowingStack(
        [Values] bool hooks,
        [Values] bool suspend
    )
    {
        using var state = CreateState();
        int? firstBase = null;
        var calls = 0;
        var t = new LuaTable { Metatable = new LuaTable() };
        t.Metatable["__newindex"] = new LuaFunction(
            async (context, token) =>
            {
                firstBase ??= context.ReturnFrameBase;
                Assert.That(context.ReturnFrameBase, Is.EqualTo(firstBase.Value));
                calls++;
                if (suspend)
                    await Task.Yield();
                return context.Return(42, 99);
            }
        );
        state.Environment["t"] = t;
        if (hooks)
            await state.DoStringAsync("debug.sethook(function() return 123 end,'cr')");
        await state.DoStringAsync("local a=t; for i=1,200 do a.missing=i end");
        Assert.That(calls, Is.EqualTo(200));
        Assert.That(state.Stack.Count, Is.Zero);
    }

    [Test]
    public async Task GlobalIndexKeepsOnlyOneResult([Values] bool callHooks, [Values] bool suspend)
    {
        using var state = CreateState();
        state.Environment.Metatable = new LuaTable
        {
            ["__index"] = new LuaFunction(
                async (context, token) =>
                {
                    if (suspend)
                        await Task.Yield();
                    return context.Return(42, 99);
                }
            ),
        };
        var observed = false;
        state.SetHook(
            new LuaFunction(
                (context, token) =>
                {
                    if (
                        context.GetArgument(0) == new LuaValue("line")
                        && context.GetArgument(1) == new LuaValue(2)
                    )
                    {
                        observed = true;
                        Assert.That(context.ReturnFrameBase, Is.EqualTo(1));
                    }
                    return new(context.Return());
                }
            ),
            callHooks ? "lcr" : "l"
        );
        var results = new LuaValue[1];
        await state.ExecuteAsync(state.Load("local x=missing\nreturn x", "global-index"), results);
        Assert.That(results[0], Is.EqualTo(new LuaValue(42)));
        Assert.That(observed, Is.True);
    }

    [Test]
    public async Task InstructionHookDiscardsResultsWithoutGrowingStack([Values] bool suspend)
    {
        using var state = CreateState();
        var calls = 0;
        state.SetHook(
            new LuaFunction(
                async (context, token) =>
                {
                    Assert.That(context.ReturnFrameBase, Is.LessThan(20));
                    calls++;
                    if (suspend)
                        await Task.Yield();
                    return context.Return(42, 99);
                }
            ),
            "",
            1
        );
        await state.DoStringAsync("local s=0; for i=1,200 do s=s+i end; return s");
        Assert.That(calls, Is.GreaterThan(200));
        Assert.That(state.Stack.Count, Is.Zero);
    }

    [TestCase("event,...")]
    [TestCase("...")]
    public async Task SuspendedVariadicInstructionHookPreservesCaller(string parameters)
    {
        using var state = CreateState();
        state.Environment["pause"] = new LuaFunction(
            async (context, token) =>
            {
                await Task.Yield();
                return context.Return();
            }
        );
        await state.DoStringAsync(
            $$"""
            debug.sethook(function({{parameters}}) pause(); return 42,99 end,'',1)
            local s=0
            for i=1,20 do s=s+i end
            debug.sethook()
            assert(s==210)
            """
        );
        Assert.That(state.Stack.Count, Is.Zero);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task ArithmeticApiInvokesOnceAndRestoresPrefix(bool hooks, bool variadic)
    {
        using var state = CreateState();
        var parameters = variadic ? "a,..." : "a,b";
        var unpack = variadic ? "local b=..." : "";
        await state.DoStringAsync(
            $$"""
            calls=0
            local function op({{parameters}})
                {{unpack}}
                calls=calls+1
                return a.x+b.x
            end
            t=setmetatable({x=3},{__add=op,__unm=op})
            """
        );
        if (hooks)
            await state.DoStringAsync("debug.sethook(function() end,'cr')");
        state.Push(12345);
        var t = state.Environment["t"];
        Assert.That((await state.AddAsync(t, t)).Read<double>(), Is.EqualTo(6));
        Assert.That(state.Environment["calls"].Read<double>(), Is.EqualTo(1));
        Assert.That(state.Stack.Count, Is.EqualTo(1));
        Assert.That((await state.UnmAsync(t)).Read<double>(), Is.EqualTo(6));
        Assert.That(state.Environment["calls"].Read<double>(), Is.EqualTo(2));
        Assert.That(state.Stack.Count, Is.EqualTo(1));
        Assert.That(state.Stack.Pop().Read<double>(), Is.EqualTo(12345));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ArithmeticApiRestoresPrefixOnError(bool unary)
    {
        using var state = CreateState();
        await state.DoStringAsync(
            "t=setmetatable({}, {__add=function(a,...) error('failure') end,__unm=function(a,...) error('failure') end})"
        );
        state.Push(12345);
        var t = state.Environment["t"];
        Assert.ThrowsAsync<LuaRuntimeException>(async () =>
        {
            if (unary)
                await state.UnmAsync(t);
            else
                await state.AddAsync(t, t);
        });
        Assert.That(state.Stack.Count, Is.EqualTo(1));
        Assert.That(state.Stack.Pop().Read<double>(), Is.EqualTo(12345));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LengthApiHonorsTableMetamethod(bool hooks)
    {
        using var state = CreateState();
        await state.DoStringAsync(
            "calls=0; t=setmetatable({1},{__len=function(a,...) calls=calls+1; assert(select('#',...)==1); return 42 end})"
        );
        if (hooks)
            await state.DoStringAsync("debug.sethook(function() end,'cr')");
        Assert.That((await state.LenAsync(state.Environment["t"])).Read<double>(), Is.EqualTo(42));
        Assert.That(state.Environment["calls"].Read<double>(), Is.EqualTo(1));
        Assert.That(state.Stack.Count, Is.Zero);
    }

    [TestCase(0)]
    [TestCase(1)]
    public async Task ConcatApiAcceptsZeroOrOneOperand(int count)
    {
        using var state = CreateState();
        state.Push(12345);
        if (count == 1)
            state.Push(42);
        var value = await state.ConcatAsync(count);
        Assert.That(value, Is.EqualTo(count == 0 ? new LuaValue("") : new LuaValue(42)));
        Assert.That(state.Stack.Count, Is.EqualTo(2));
        Assert.That(state.Stack.Pop(), Is.EqualTo(value));
        Assert.That(state.Stack.Pop().Read<double>(), Is.EqualTo(12345));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ConcatMetamethodCanReturnNoValues(bool hooks)
    {
        using var state = CreateState();
        if (hooks)
            await state.DoStringAsync("debug.sethook(function() end,'cr')");
        var result = await state.DoStringAsync(
            "local t=setmetatable({}, {__concat=function() end}); local x=t..t; return x==nil"
        );
        Assert.That(result[0].Read<bool>(), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RunAsyncPadsMissingFixedArguments(bool hooks)
    {
        using var state = CreateState();
        var functions = await state.DoStringAsync(
            "return function(a,b,...) return a,b,select('#',...) end"
        );
        if (hooks)
            await state.DoStringAsync("debug.sethook(function() end,'cr')");
        state.Push(7);
        var count = await state.RunAsync(functions[0].Read<LuaFunction>(), 1);
        using var values = state.ReadStack(count);
        Assert.That(values.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { 7, LuaValue.Nil, 0 }));
    }

    [Test]
    public async Task HookPadsMissingFixedArguments()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            calls=0
            debug.sethook(function(event,line,extra,...)
                assert(type(event)=='string' and line==nil and extra==nil and select('#',...)==0)
                calls=calls+1
            end,'cr')
            local function f() end
            f()
            debug.sethook()
            assert(calls>0)
            """
        );
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task NativeOperationHonorsZeroResults(bool hooks, bool suspend)
    {
        using var state = CreateState();
        state.Environment["operation"] = new LuaFunction(
            async (context, token) =>
            {
                if (suspend)
                    await Task.Yield();
                return context.Return();
            }
        );
        await state.DoStringAsync(
            "t=setmetatable({}, {__add=operation,__unm=operation,__lt=operation,__concat=operation})"
        );
        if (hooks)
            await state.DoStringAsync("debug.sethook(function() end,'cr')");
        state.Push(12345);
        var t = state.Environment["t"];
        Assert.That(await state.AddAsync(t, t), Is.EqualTo(LuaValue.Nil));
        Assert.That(await state.UnmAsync(t), Is.EqualTo(LuaValue.Nil));
        Assert.That(await state.LessThanAsync(t, t), Is.False);
        Assert.That(await state.GetTableAsync(t, "missing"), Is.EqualTo(LuaValue.Nil));
        Assert.That(state.Stack.Count, Is.EqualTo(1));
        Assert.That(await state.ConcatAsync([t, t]), Is.EqualTo(LuaValue.Nil));
        Assert.That(state.Stack.Count, Is.EqualTo(2));
        state.Stack.Pop();
        var values = await state.DoStringAsync("local x=t..t; return x==nil");
        Assert.That(values[0].Read<bool>(), Is.True);
        Assert.That(state.Stack.Count, Is.EqualTo(1));
        Assert.That(state.Stack.Pop().Read<double>(), Is.EqualTo(12345));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task VmNativeZeroResultsDoNotReuseArguments(bool hooks, bool suspend)
    {
        using var state = CreateState();
        state.Environment["zero"] = new LuaFunction(
            async (context, token) =>
            {
                if (suspend)
                    await Task.Yield();
                return context.Return();
            }
        );
        await state.DoStringAsync(
            "t=setmetatable({}, {__add=zero,__unm=zero,__lt=zero,__index=zero})"
        );
        if (hooks)
            await state.DoStringAsync("debug.sethook(function() end,'cr')");
        await state.DoStringAsync(
            """
            local x=zero(42)
            assert(x==nil)
            local function tail() return zero(42) end
            assert(select('#',tail())==0)
            assert(t+t==nil and -t==nil and not(t<t) and t<=t)
            assert(t.missing==nil)
            local ok=pcall(function() t:missing() end)
            assert(not ok)
            local count=0
            for k in zero,42,99 do
                count=count+1
                assert(count<2)
            end
            assert(count==0)
            """
        );
        var result = await state.CallAsync(state.Environment["zero"], [42]);
        Assert.That(result, Is.Empty);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [Test]
    public async Task CanceledArithmeticApiRestoresPrefixBeforeInvocation()
    {
        using var state = CreateState();
        var calls = 0;
        var t = new LuaTable { Metatable = new LuaTable() };
        t.Metatable["__add"] = new LuaFunction(
            (context, token) =>
            {
                calls++;
                return new(context.Return(42));
            }
        );
        state.Push(12345);
        Assert.ThrowsAsync<LuaCanceledException>(async () =>
            await state.AddAsync(t, t, new CancellationToken(true))
        );
        Assert.That(calls, Is.Zero);
        Assert.That(state.Stack.Count, Is.EqualTo(1));
        Assert.That(state.Stack.Pop().Read<double>(), Is.EqualTo(12345));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [Test]
    public async Task ConcatCombinesEmptyStringsAndNumbersInOrder()
    {
        using var state = CreateState();
        state.Push(12345);
        Assert.That(
            (await state.ConcatAsync([1, "", 2, "c", 3])).Read<string>(),
            Is.EqualTo("12c3")
        );
        Assert.That(state.Stack.Count, Is.EqualTo(2));
        Assert.That(state.Stack.Pop().Read<string>(), Is.EqualTo("12c3"));
        Assert.That(state.Stack.Pop().Read<double>(), Is.EqualTo(12345));
        var result = await state.DoStringAsync(
            """
            local calls=0
            local t=setmetatable({}, {__concat=function(a,b)
                calls=calls+1
                if calls==2 then assert(b==nil); return 'done' end
            end})
            return t..t..t
            """
        );
        Assert.That(result[0].Read<string>(), Is.EqualTo("done"));
    }

    [TestCase(-1)]
    [TestCase(2)]
    public void ConcatRejectsInvalidCountWithoutChangingStack(int count)
    {
        using var state = CreateState();
        state.Push(12345);
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await state.ConcatAsync(count));
        Assert.That(state.Stack.Count, Is.EqualTo(1));
        Assert.That(state.Stack.Pop().Read<double>(), Is.EqualTo(12345));
    }

    [Test]
    public void ArithmeticSetupErrorsRemainFaultedValueTasks()
    {
        using var state = CreateState();
        ValueTask<LuaValue> binary = default,
            unary = default;
        Assert.DoesNotThrow(() => binary = state.AddAsync(true, false));
        Assert.DoesNotThrow(() => unary = state.UnmAsync(true));
        Assert.ThrowsAsync<LuaRuntimeException>(async () => await binary);
        Assert.ThrowsAsync<LuaRuntimeException>(async () => await unary);
    }
}
