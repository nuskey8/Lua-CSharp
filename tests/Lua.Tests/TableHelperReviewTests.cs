using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class TableHelperReviewTests(LuaStringMode mode)
{
    LuaState CreateState()
    {
        var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        return state;
    }

    [TestCase("table.insert(t,0.5,99)")]
    [TestCase("table.remove(t,0.5)")]
    [TestCase("table.insert(t,0/0,99)")]
    [TestCase("table.remove(t,math.huge)")]
    public async Task InvalidPositionsAreStillRejected(string operation)
    {
        using var state = CreateState();
        var result = await state.DoStringAsync(
            "local t={11,22,33}; return pcall(function() " + operation + " end)"
        );
        Assert.That(result[0].Read<bool>(), Is.False);
    }

    [TestCase(1, 8)]
    [TestCase(2, 7)]
    [TestCase(0, 4)]
    [TestCase(-2, 2)]
    [TestCase(7, 10)]
    [TestCase(9, 8)]
    public async Task UnpackPreservesRawValuesAcrossArrayAndHashRanges(int first, int last)
    {
        using var state = CreateState();
        var table = new LuaTable(8, 8);
        for (var i = -2; i <= 10; i++)
            table[i] = i % 3 == 0 ? LuaValue.Nil : i * 10;
        table.Metatable = new LuaTable
        {
            ["__index"] = new LuaFunction(
                (ctx, _) => throw new Exception("unpack must use raw access")
            ),
        };
        var unpack = state.Environment["table"].Read<LuaTable>()["unpack"];
        var values = await state.CallAsync(unpack, [table, first, last]);
        Assert.That(values.Length, Is.EqualTo(Math.Max(0, last - first + 1)));
        for (var i = 0; i < values.Length; i++)
            Assert.That(values[i], Is.EqualTo(table[first + i]));
        Assert.That(state.Stack.Count, Is.Zero);
    }

    [Test]
    public async Task UnpackReadsRetainedWritableArrayChanges()
    {
        using var state = CreateState();
        var table = new LuaTable(8, 0);
        table[1] = 11;
        table[2] = 22;
        table[3] = 33;
        var unpack = state.Environment["table"].Read<LuaTable>()["unpack"];
        Assert.That((await state.CallAsync(unpack, [table])).Length, Is.EqualTo(3));
        var memory = table.GetArrayMemory();
        memory.Span[1] = LuaValue.Nil;
        var values = await state.CallAsync(unpack, [table, 1, 3]);
        Assert.That(values, Is.EqualTo(new LuaValue[] { 11, LuaValue.Nil, 33 }));
        Assert.That(table.ArrayLength, Is.EqualTo(1));
    }

    [Test]
    public async Task SingleValueAtMaximumLongIndexDoesNotWrapItsLoop()
    {
        using var state = CreateState();
        var table = new LuaTable(0, 1);
        table[(double)long.MaxValue] = "last";
        var library = state.Environment["table"].Read<LuaTable>();
        var values = await state.CallAsync(
            library["unpack"],
            [table, (double)long.MaxValue, (double)long.MaxValue]
        );
        Assert.That(values, Is.EqualTo(new LuaValue[] { "last" }));
        values = await state.CallAsync(
            library["concat"],
            [table, ",", (double)long.MaxValue, (double)long.MaxValue]
        );
        Assert.That(values[0].Read<string>(), Is.EqualTo("last"));
    }

    [TestCase("1,1500000000")]
    [TestCase("1,2147483647")]
    [TestCase("1,4294967297")]
    [TestCase("-9223372036854775808,9223372036854775808")]
    public async Task ExcessiveUnpackRangesAreRejectedWithoutStackGrowth(string range)
    {
        using var state = CreateState();
        var values = await state.DoStringAsync("return pcall(table.unpack,{}," + range + ")");
        Assert.That(values[0].Read<bool>(), Is.False);
        Assert.That(values[1].Read<string>(), Does.Contain("too many results to unpack"));
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That((await state.DoStringAsync("return 42"))[0].Read<double>(), Is.EqualTo(42));
    }

    [Test]
    public void EmptyStackCanGrowAndRejectsOverflowingCapacity()
    {
        var stack = new LuaStack(0);
        stack.Push(42);
        Assert.That(stack.Pop().Read<double>(), Is.EqualTo(42));
        Assert.Throws<LuaStackOverflowException>(() => stack.EnsureCapacity(int.MaxValue));
        Assert.That(stack.Count, Is.Zero);
    }
}
