using Lua.Standard;

namespace Lua.Tests;

public class BitwiseLibraryTests
{
    static LuaState CreateState()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        return state;
    }

    // Expected values checked against native Lua 5.2, including signed-count boundaries.
    [TestCase("arshift(1,32)", 0d)]
    [TestCase("arshift(0x80000000,32)", 4294967295d)]
    [TestCase("arshift(0x89abcdef,33)", 4294967295d)]
    [TestCase("arshift(0x89abcdef,65)", 4294967295d)]
    [TestCase("arshift(0x89abcdef,-32)", 0d)]
    [TestCase("arshift(1,-33)", 0d)]
    [TestCase("arshift(1,-2147483648)", 0d)]
    [TestCase("arshift(0x80000000,2147483647)", 4294967295d)]
    [TestCase("arshift(0x80000000,31)", 4294967295d)]
    [TestCase("arshift(0xffffffff,-1)", 4294967294d)]
    [TestCase("lshift(1,-2147483648)", 0d)]
    [TestCase("rshift(1,-2147483648)", 0d)]
    [TestCase("lshift(0xffffffff,31)", 2147483648d)]
    [TestCase("rshift(0xffffffff,31)", 1d)]
    [TestCase("lrotate(0x89abcdef,-4)", 4170890462d)]
    [TestCase("rrotate(0x89abcdef,4)", 4170890462d)]
    [TestCase("lrotate(0x89abcdef,2147483647)", 3302352631d)]
    [TestCase("rrotate(0x89abcdef,-2147483648)", 2309737967d)]
    public async Task ShiftAndRotationBoundariesAgreeWithLua52(string expression, double expected)
    {
        using var state = CreateState();
        var values = await state.DoStringAsync("return bit32." + expression);
        Assert.That(values[0].Read<double>(), Is.EqualTo(expected));
    }

    [Test]
    public async Task BitNormalizationWrapsSignedIntegersWithoutLosingLowBits()
    {
        using var state = CreateState();
        var band = state.Environment["bit32"].Read<LuaTable>()["band"];
        long[] values =
        [
            0,
            1,
            -1,
            int.MinValue,
            int.MaxValue,
            uint.MaxValue,
            4294967296,
            -4294967297,
            (1L << 51) - 1,
            -(1L << 51) + 1,
        ];
        foreach (var value in values)
        {
            var result = await state.CallAsync(band, [(double)value]);
            Assert.That(
                result[0].Read<double>(),
                Is.EqualTo((double)unchecked((uint)value)),
                value.ToString()
            );
        }
        await state.DoStringAsync(
            "assert(bit32.band()==0xffffffff and bit32.bor()==0 and bit32.btest())"
        );
    }

    [TestCase("extract(0,0,0)", 3)]
    [TestCase("replace(0,0,0,0)", 4)]
    [TestCase("replace(0,0,-1,1)", 3)]
    public async Task FieldErrorsIdentifyTheCorrectArgument(string expression, int argument)
    {
        using var state = CreateState();
        var result = await state.DoStringAsync(
            "local ok,e=pcall(function() return bit32." + expression + " end); return ok,e"
        );
        Assert.That(result[0].Read<bool>(), Is.False);
        Assert.That(result[1].Read<string>(), Does.Contain("bad argument #" + argument));
    }

    [TestCase("extract(0,31,2147483647)")]
    [TestCase("replace(0,0,31,2147483647)")]
    public async Task FieldWidthCannotOverflowValidation(string expression)
    {
        using var state = CreateState();
        var result = await state.DoStringAsync(
            "return pcall(function() return bit32." + expression + " end)"
        );
        Assert.That(result[0].Read<bool>(), Is.False);
        Assert.That(result[1].Read<string>(), Does.Contain("non-existent bits"));
    }

    [TestCase("band(0.5)", 0d)]
    [TestCase("band(1.5)", 2d)]
    [TestCase("band(2.5)", 2d)]
    [TestCase("band(-1.5)", 4294967294d)]
    [TestCase("band('1.5',3)", 2d)]
    [TestCase("band(4294967295.5)", 0d)]
    [TestCase("bnot(1.5)", 4294967293d)]
    [TestCase("bor(1.5,0.5)", 2d)]
    [TestCase("bxor(1.5,2.5)", 0d)]
    [TestCase("lshift(1.5,1.5)", 4d)]
    [TestCase("rshift(1.5,-1.5)", 4d)]
    [TestCase("arshift(1.5,1.5)", 1d)]
    [TestCase("lrotate(1.5,-1.5)", 1d)]
    [TestCase("rrotate(1.5,-1.5)", 4d)]
    [TestCase("extract(15.5,1.5,2.9)", 0d)]
    [TestCase("replace(0.5,3.5,1.5,2.9)", 0d)]
    [TestCase("extract(0xffffffff,0,32)", 4294967295d)]
    [TestCase("replace(0,0xffffffff,0,32)", 4294967295d)]
    public async Task FractionalWordsAndIntegerArgumentsFollowLua52(
        string expression,
        double expected
    )
    {
        using var state = CreateState();
        var result = await state.DoStringAsync("return bit32." + expression);
        Assert.That(result[0].Read<double>(), Is.EqualTo(expected));
        await state.DoStringAsync("assert(not bit32.btest(0.5)); assert(bit32.btest(1.5))");
    }
}
