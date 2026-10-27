using Lua.Standard;

namespace Lua.Tests;

public class ArithmeticExecutionRegressionTests
{
    static LuaState CreateState()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        return state;
    }

    [TestCase("+", 10)]
    [TestCase("-", 4)]
    [TestCase("*", 21)]
    [TestCase("/", 7.0 / 3)]
    [TestCase("%", 1)]
    [TestCase("^", 343)]
    public async Task ArithmeticKeepsCoercionAndOperandAliasing(string op, double expected)
    {
        using var state = CreateState();
        var values = await state.DoStringAsync(
            $"""
            local a, b = 7, 3
            local number = a {op} b
            a = '7'
            a = a {op} b
            b = '3'
            b = 7 {op} b
            return number, a, b
            """
        );
        Assert.That(values.Select(v => v.Read<double>()), Is.All.EqualTo(expected));
    }

    [TestCase("+", "__add")]
    [TestCase("-", "__sub")]
    [TestCase("*", "__mul")]
    [TestCase("/", "__div")]
    [TestCase("%", "__mod")]
    [TestCase("^", "__pow")]
    public async Task ArithmeticKeepsYieldingMetamethods(string op, string metamethod)
    {
        using var state = CreateState();
        await state.DoStringAsync(
            $$"""
            local mt = { {{metamethod}} = function(a, b)
                coroutine.yield(12)
                return a.x + b.x
            end }
            local a = setmetatable({x=3}, mt)
            local b = setmetatable({x=4}, mt)
            local co = coroutine.create(function() return a {{op}} b end)
            local ok, value = coroutine.resume(co)
            assert(ok and value == 12)
            ok, value = coroutine.resume(co)
            assert(ok and value == 7)
            """
        );
    }

    [Test]
    public async Task ArithmeticBytecodeRoundTripAndSpecialNumbers()
    {
        using var state = CreateState();
        const string source = """
            local a, b = -3, 2
            assert(a % b == 1)
            a, b = 3, -2
            assert(a % b == -1)
            local inf = math.huge
            local nan = inf - inf
            assert(nan ~= nan)
            assert(inf + 1 == inf)
            local zero = -0.0
            assert(1 / (zero * 2) == -inf)
            local x = 2
            x = x ^ 3
            return x
            """;
        var closure = state.Load(source, "arithmetic-roundtrip");
        var bytecode = closure.Proto.ToBytecode();
        var restored = state.Load(bytecode, "arithmetic-roundtrip");
        var results = await state.ExecuteAsync(restored);
        Assert.That(results[0].Read<double>(), Is.EqualTo(8));
    }

    [Test]
    public async Task ZeroStepNumericForUsesLua52Direction()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            local count = 0
            for i = 1, 2, 0 do count = count + 1; break end
            assert(count == 0)
            for i = 2, 1, 0 do count = count + 1; break end
            assert(count == 1)
            for i = 1, 1, -0.0 do count = count + 1; break end
            assert(count == 2)
            """
        );
    }
}
