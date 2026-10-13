using Lua.Standard;

namespace Lua.Tests;

public class PatternClassTests
{
    const string SingleElementArray = """{"SomeArray":["Alice"], "Text":"some_text"}""";
    const string MultipleElementArray = """{"SomeArray":["Alice","Bob","Denis"], "Text":"some_text"}""";

    [TestCase(SingleElementArray, "%[[^%]]*,", -1, -1)]
    [TestCase(MultipleElementArray, "%[[^%]]*,", 14, 28)]
    [TestCase(SingleElementArray, "[[^%]]*", 1, 0)]
    [TestCase(SingleElementArray, "[^%]]*", 1, 21)]
    [TestCase("[ab,c]", "%[[^%]]*,", 1, 4)]
    [TestCase("abc", "[[^%]]*", 1, 0)]
    [TestCase("]", "[%]]", 1, 1)]
    [TestCase("]]x", "[%]]+", 1, 2)]
    [TestCase("abc]", "[^%]]+", 1, 3)]
    [TestCase("]", "[a%]]", 1, 1)]
    [TestCase("]", "[%%%]]", 1, 1)]
    [TestCase("]", "[]]", 1, 1)]
    [TestCase("abc]", "[^]]+", 1, 3)]
    [TestCase("[", "[[]", 1, 1)]
    [TestCase("12x", "[%d]+", 1, 2)]
    [TestCase("%", "[%%]", 1, 1)]
    public async Task FindCharacterClass(string source, string pattern, int start, int end)
    {
        using var state = LuaState.Create();
        state.OpenStringLibrary();

        var result = await state.DoStringAsync($"return string.find('{source}', '{pattern}')");

        if (start < 0)
        {
            Assert.That(result, Is.EqualTo(new LuaValue[] { LuaValue.Nil }));
            return;
        }

        Assert.That(result, Is.EqualTo(new LuaValue[] { start, end }));
    }

    [TestCase("[%]")]
    [TestCase("[^%]")]
    [TestCase("[")]
    [TestCase("[^")]
    [TestCase("[%")]
    [TestCase("[]")]
    public void RejectUnterminatedCharacterClass(string pattern)
    {
        using var state = LuaState.Create();
        state.OpenStringLibrary();

        var exception = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.DoStringAsync($"return string.find('x]', '{pattern}')")
        );

        Assert.That(exception!.Message, Does.Contain("malformed pattern (missing ']')"));
    }

    [Test]
    public async Task EscapedClosingBracketInMatchGMatchAndGSub()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();

        var result = await state.DoStringAsync(
            """
            local s = '[ab,c]'
            local p = '%[[^%]]*,'
            local f = string.gmatch(s, p)
            local replaced, count = string.gsub(s, p, 'X')
            return string.match(s, p), f(), replaced, count
            """
        );

        Assert.That(result, Is.EqualTo(new LuaValue[] { "[ab,", "[ab,", "Xc]", 1 }));
    }

    [Test]
    public async Task EscapedClosingBracketInFrontier()
    {
        using var state = LuaState.Create();
        state.OpenStringLibrary();

        var result = await state.DoStringAsync("return string.find(']a', '%f[%]]')");

        Assert.That(result, Is.EqualTo(new LuaValue[] { 1, 0 }));
    }
}
