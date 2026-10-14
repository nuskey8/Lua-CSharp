using Lua.Standard;

namespace Lua.Tests;

public class GSubMetatableTests
{
    [TestCase(
        "local r,n = string.gsub('ab', '.', repl); assert(r == 'AB' and n == 2 and calls == 2)"
    )]
    [TestCase(
        "repl.a = 'raw'; local r,n = string.gsub('ab', '.', repl); assert(r == 'rawB' and n == 2 and calls == 1)"
    )]
    [TestCase(
        "local r,n = string.gsub('ab', '(a)(b)', repl); assert(r == 'A' and n == 1 and calls == 1)"
    )]
    [TestCase(
        "local r,n = string.gsub('ab', '.', repl, 1); assert(r == 'Ab' and n == 1 and calls == 1)"
    )]
    public async Task Test_FunctionIndex(string script)
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        await state.DoStringAsync(
            """
            calls = 0
            repl = setmetatable({}, {__index = function(self, key)
                assert(self == repl)
                calls = calls + 1
                return string.upper(key)
            end})
            """ + script
        );
    }

    [Test]
    public async Task Test_TableIndexChain()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        await state.DoStringAsync(
            """
            local fallback = setmetatable({a = 'A'}, {__index = {b = 'B'}})
            local repl = setmetatable({}, {__index = fallback})
            local r,n = string.gsub('abc', '.', repl)
            assert(r == 'ABc' and n == 3)
            """
        );
    }

    [TestCase("nil", "a")]
    [TestCase("false", "a")]
    [TestCase("42", "42")]
    public async Task Test_IndexResult(string expression, string expected)
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        var results = await state.DoStringAsync(
            "local repl = setmetatable({}, {__index = function() return "
                + expression
                + " end}); return string.gsub('a', '.', repl)"
        );
        Assert.That(results[0].Read<string>(), Is.EqualTo(expected));
        Assert.That(results[1].Read<double>(), Is.EqualTo(1));
    }

    [TestCase("error('index failed', 0)", "index failed")]
    [TestCase("return {}", "invalid replacement value")]
    public async Task Test_IndexError(string body, string expected)
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        var results = await state.DoStringAsync(
            "local repl = setmetatable({}, {__index = function() "
                + body
                + " end}); return pcall(string.gsub, 'a', '.', repl)"
        );
        Assert.That(results[0].Read<bool>(), Is.False);
        Assert.That(results[1].Read<string>(), Does.Contain(expected));
        var next = await state.DoStringAsync("return string.gsub('a', '.', {a='ok'})");
        Assert.That(next[0].Read<string>(), Is.EqualTo("ok"));
    }

    [Test]
    public async Task Test_AsyncIndex()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        var calls = 0;
        state.Environment["index"] = new LuaFunction(
            "index",
            async (context, ct) =>
            {
                var key = context.GetArgument<string>(1);
                await Task.Yield();
                ct.ThrowIfCancellationRequested();
                calls++;
                return context.Return(key.ToUpperInvariant());
            }
        );
        var results = await state.DoStringAsync(
            "return string.gsub('ab', '.', setmetatable({}, {__index = index}))"
        );
        Assert.That(results[0].Read<string>(), Is.EqualTo("AB"));
        Assert.That(results[1].Read<double>(), Is.EqualTo(2));
        Assert.That(calls, Is.EqualTo(2));
    }
}
