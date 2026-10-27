using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

public class NativeCallbackResultTests
{
    static LuaState CreateState()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        return state;
    }

    [TestCase(0, 1, 2)]
    [TestCase(-1, 1, 2)]
    [TestCase(2, 1, 2)]
    [TestCase(1, 0, 4)]
    [TestCase(1, -1, 4)]
    [TestCase(1, 2, 4)]
    public async Task UpvalueJoinRejectsInvalidIndices(int first, int second, int argument)
    {
        using var state = CreateState();
        var results = await state.DoStringAsync(
            $"""
            local a,b=1,2
            local function f() return a end
            local function g() return b end
            return pcall(debug.upvaluejoin,f,{first},g,{second})
            """
        );
        Assert.That(results[0].Read<bool>(), Is.False);
        Assert.That(results[1].Read<string>(), Does.Contain($"bad argument #{argument}"));
        Assert.That(results[1].Read<string>(), Does.Contain("invalid upvalue index"));
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void IndexApiRestoresPrefixWhenCallFrameLimitRejectsSetup(bool setter)
    {
        using var state = CreateState();
        var operation = new LuaFunction((context, token) => new(context.Return()));
        var table = new LuaTable
        {
            Metatable = new LuaTable { ["__index"] = operation, ["__newindex"] = operation },
        };
        state.MaxCallDepth = 1;
        state.Push(12345);
        state.PushCallStackFrame(
            new()
            {
                Function = operation,
                Base = 1,
                ReturnBase = 1,
                VariableArgumentCount = 0,
            }
        );
        try
        {
            Assert.ThrowsAsync<LuaStackOverflowException>(async () =>
            {
                if (setter)
                    await state.SetTableAsync(table, "key", 42);
                else
                    await state.GetTableAsync(table, "key");
            });
            Assert.That(state.Stack.Count, Is.EqualTo(1));
            Assert.That(state.Stack.Pop().Read<double>(), Is.EqualTo(12345));
            Assert.That(state.CallStackFrameCount, Is.EqualTo(1));
        }
        finally
        {
            state.PopCallStackFrame();
        }
    }

    [TestCase("return getmetatable,{}", 1)]
    [TestCase("return getmetatable,setmetatable({}, {})", 1)]
    [TestCase("return getmetatable,setmetatable({}, {__metatable='locked'})", 1)]
    [TestCase("return tostring,12", 1)]
    [TestCase("return tostring,nil", 1)]
    [TestCase("return tostring,false", 1)]
    [TestCase("return tostring,'text'", 1)]
    [TestCase(
        "return tostring,setmetatable({}, {__tostring=function() return 'text','extra' end})",
        1
    )]
    [TestCase("return bit32.band", 1)]
    [TestCase("return debug.sethook,nil", 0)]
    [TestCase("return debug.sethook,function() end,'cr'", 0)]
    [TestCase(
        "local a,b=1,2; return debug.upvaluejoin,function() return a end,1,function() return b end,1",
        0
    )]
    public async Task BuiltInCountsMatchTheirStackResults(string setup, int expectedCount)
    {
        foreach (var hooks in new[] { false, true })
        {
            using var state = CreateState();
            var values = await state.DoStringAsync(setup);
            if (hooks)
                await state.DoStringAsync("debug.sethook(function() end,'cr')");
            state.Push(12345);
            state.Stack.PushRange(values.AsSpan(1));
            var count = await state.RunAsync(values[0].Read<LuaFunction>(), values.Length - 1);
            Assert.That(count, Is.EqualTo(expectedCount));
            Assert.That(state.Stack.Count, Is.EqualTo(1 + count));
            using (var results = state.ReadStack(count))
                Assert.That(results.Count, Is.EqualTo(expectedCount));
            Assert.That(state.Stack.Pop().Read<double>(), Is.EqualTo(12345));
            Assert.That(state.CallStackFrameCount, Is.Zero);
        }
    }

    [Test]
    public async Task ZeroResultDebugFunctionsDoNotExposeTheirArguments()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            assert(select('#',debug.sethook(function() end,'cr'))==0)
            assert(select('#',debug.sethook(nil))==0)
            local a,b=1,2
            local function f() return a end
            local function g() return b end
            assert(select('#',debug.upvaluejoin(f,1,g,1))==0)
            assert(f()==2)
            """
        );
    }

    [TestCase("return")]
    [TestCase("return nil")]
    [TestCase("return true")]
    [TestCase("return {}")]
    public async Task ToStringKeepsOneMetamethodResultAsInLua52(string body)
    {
        using var state = CreateState();
        var values = await state.DoStringAsync(
            $"local x=setmetatable({{}}, {{__tostring=function() {body} end}}); return tostring(x)"
        );
        Assert.That(values.Length, Is.EqualTo(1));
        Assert.That(
            values[0].Type,
            Is.EqualTo(
                body switch
                {
                    "return true" => LuaValueType.Boolean,
                    "return {}" => LuaValueType.Table,
                    _ => LuaValueType.Nil,
                }
            )
        );
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [Test]
    public async Task ManagedCallbacksDistinguishNoResultsFromNil(
        [Values] bool hooks,
        [Values] bool suspend,
        [Values(0, 1, 3)] int count
    )
    {
        using var state = CreateState();
        state.Environment["callback"] = new LuaFunction(
            async (context, token) =>
            {
                if (suspend)
                    await Task.Yield();
                return count switch
                {
                    0 => context.Return(),
                    1 => context.Return(LuaValue.Nil),
                    _ => context.Return(42, LuaValue.Nil, "last"),
                };
            }
        );
        if (hooks)
            await state.DoStringAsync("debug.sethook(function() end,'cr')");
        state.Push(12345);
        state.Push(99);
        var actualCount = await state.RunAsync(
            state.Environment["callback"].Read<LuaFunction>(),
            1
        );
        Assert.That(actualCount, Is.EqualTo(count));
        Assert.That(state.Stack.Count, Is.EqualTo(1 + count));
        using (var results = state.ReadStack(count))
        {
            var expected = count switch
            {
                0 => Array.Empty<LuaValue>(),
                1 => [LuaValue.Nil],
                _ => new LuaValue[] { 42, LuaValue.Nil, "last" },
            };
            Assert.That(results.AsSpan().ToArray(), Is.EqualTo(expected));
        }
        var vm = await state.DoStringAsync("return select('#',callback(99))");
        Assert.That(vm[0].Read<double>(), Is.EqualTo(count));
        Assert.That(state.Stack.Pop().Read<double>(), Is.EqualTo(12345));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [Test]
    public async Task ToStringUsesOneStringResult([Values] bool hooks, [Values] bool suspend)
    {
        using var state = CreateState();
        state.Environment["format"] = new LuaFunction(
            async (context, token) =>
            {
                if (suspend)
                    await Task.Yield();
                return context.Return(12, "extra");
            }
        );
        if (hooks)
            await state.DoStringAsync("debug.sethook(function() end,'cr')");
        await state.DoStringAsync(
            """
            local x=setmetatable({}, {__tostring=format})
            assert(select('#',tostring(x))==1)
            assert(tostring(x)=='12')
            assert(string.format('%s:%s',x,'tail')=='12:tail')
            """
        );
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }
}
