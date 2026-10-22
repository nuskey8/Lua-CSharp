using Lua.Standard;

namespace Lua.Tests;

// Expected values come from the reference Lua 5.2.4 interpreter.
public class GSubReplacementStringTests
{
    [TestCase("abc", "(a)(b)", "%1%2%%", "ab%c", 1)]
    [TestCase("a%1b", "(%%1)", "[%1]", "a[%1]b", 1)]
    [TestCase("x%1y", "%%1", "<%0>%0", "x<%1>%1y", 1)]
    [TestCase("abc", "()b", "%1", "a2c", 1)]
    [TestCase("hello world", "(o)", "%1%1", "helloo woorld", 2)]
    [TestCase("abc", "", "-", "-a-b-c-", 4)]
    [TestCase("abc", "b", "%1", "abc", 1)]
    public async Task Test_ReplacementString(
        string source,
        string pattern,
        string replacement,
        string expected,
        int count
    )
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["source"] = source;
        state.Environment["pattern"] = pattern;
        state.Environment["replacement"] = replacement;
        var results = await state.DoStringAsync("return string.gsub(source, pattern, replacement)");

        Assert.That(results[0].Read<string>(), Is.EqualTo(expected));
        Assert.That(results[1].Read<int>(), Is.EqualTo(count));
    }

    [TestCase("function", "%f[^\u0001-\u00ff]", "function.", 1)]
    [TestCase("01abc45de3", "%f[%d]", ".01abc.45de.3", 3)]
    [TestCase("abc", "%f[%z]", "abc.", 1)]
    public async Task Test_FrontierAtEndOfSubject(
        string source,
        string pattern,
        string expected,
        int count
    )
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["source"] = source;
        state.Environment["pattern"] = pattern;
        var results = await state.DoStringAsync("return string.gsub(source, pattern, '.')");

        Assert.That(results[0].Read<string>(), Is.EqualTo(expected));
        Assert.That(results[1].Read<int>(), Is.EqualTo(count));
    }

    [Test]
    public async Task Test_NulInCaptureIsKept()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        var results = await state.DoStringAsync("return string.gsub('a\\0b', '(.)', '%1')");

        Assert.That(results[0].Read<string>(), Is.EqualTo("a\0b"));
        Assert.That(results[1].Read<int>(), Is.EqualTo(3));
    }

    [TestCase("alo", "(.)", "%2", "invalid capture index")]
    [TestCase("alo", ".", "%2", "invalid capture index")]
    [TestCase("abc", "b", "%x", "invalid use of '%' in replacement string")]
    [TestCase("abc", "b", "%", "invalid use of '%' in replacement string")]
    public async Task Test_InvalidReplacementString(
        string source,
        string pattern,
        string replacement,
        string message
    )
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["source"] = source;
        state.Environment["pattern"] = pattern;
        state.Environment["replacement"] = replacement;
        var results = await state.DoStringAsync(
            "return pcall(string.gsub, source, pattern, replacement)"
        );

        Assert.That(results[0].Read<bool>(), Is.False);
        Assert.That(results[1].Read<string>(), Does.Contain(message));
    }

    [TestCase("%f")]
    [TestCase("%fa")]
    public async Task Test_FrontierWithoutSet(string pattern)
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["pattern"] = pattern;
        var results = await state.DoStringAsync("return pcall(string.find, 'a', pattern)");

        Assert.That(results[0].Read<bool>(), Is.False);
        Assert.That(results[1].Read<string>(), Does.Contain("missing '[' after '%f' in pattern"));
    }
}
