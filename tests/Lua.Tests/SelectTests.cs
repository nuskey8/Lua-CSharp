using Lua.Standard;

namespace Lua.Tests;

public class SelectTests
{
    static LuaState CreateState()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        return state;
    }

    [TestCase("1", 2)]
    [TestCase("2", 1)]
    [TestCase("3", 0)]
    [TestCase("4", 0)]
    [TestCase("2147483647", 0)]
    [TestCase("-1", 1)]
    [TestCase("-2", 2)]
    [TestCase("1.5", 2)]
    [TestCase("'2'", 1)]
    public async Task SelectReturnsTheExpectedNumberOfValues(string selector, int count)
    {
        using var state = CreateState();
        var values = await state.DoStringAsync($"return select({selector},'a','b')");
        Assert.That(values.Length, Is.EqualTo(count));
        if (count > 0)
            Assert.That(values[^1].Read<string>(), Is.EqualTo("b"));
        Assert.That(state.Stack.Count, Is.Zero);
    }

    [TestCase("0")]
    [TestCase("-3")]
    [TestCase("-2147483648")]
    [TestCase("0.5")]
    [TestCase("0/0")]
    [TestCase("math.huge")]
    public async Task SelectRejectsInvalidIndicesWithLuaRangeErrors(string selector)
    {
        using var state = CreateState();
        var values = await state.DoStringAsync($"return pcall(select,{selector},'a','b')");
        Assert.That(values[0].Read<bool>(), Is.False);
        Assert.That(values[1].Read<string>(), Does.Contain("index out of range"));
    }

    [Test]
    public async Task SelectPreservesNilValuesAndRecognizesCountPrefixes()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            assert(select('#anything','a',nil)==2)
            assert(select('#anything')==0)
            local t=table.pack(select(2,'skip',nil,3,nil))
            assert(t.n==3 and t[1]==nil and t[2]==3 and t[3]==nil)
            """
        );
    }
}
