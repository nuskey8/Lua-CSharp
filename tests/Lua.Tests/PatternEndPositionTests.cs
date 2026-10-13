using Lua.Standard;

namespace Lua.Tests;

public class PatternEndPositionTests
{
    [TestCase("abc", "$", 1, 4, 3)]
    [TestCase("abc", "$", -1, 4, 3)]
    [TestCase("abc", "a*", 4, 4, 3)]
    [TestCase("abc", ".*", 4, 4, 3)]
    [TestCase("", "$", 1, 1, 0)]
    [TestCase("", "^$", 1, 1, 0)]
    [TestCase("", "^", 1, 1, 0)]
    [TestCase("", "a*", 1, 1, 0)]
    [TestCase("", "a?", 1, 1, 0)]
    [TestCase("", "a-", 1, 1, 0)]
    public async Task FindEmptyMatch(string source, string pattern, int init, int start, int end)
    {
        using var state = LuaState.Create();
        state.OpenStringLibrary();

        var result = await state.DoStringAsync(
            $"return string.find('{source}', '{pattern}', {init})"
        );

        Assert.That(result, Is.EqualTo(new LuaValue[] { start, end }));
    }

    [TestCase("abc", "$", 1)]
    [TestCase("abc", "a*", 4)]
    [TestCase("abc", ".*", 4)]
    [TestCase("", "$", 1)]
    [TestCase("", "^$", 1)]
    [TestCase("", "^", 1)]
    [TestCase("", "a*", 1)]
    [TestCase("", "a?", 1)]
    [TestCase("", "a-", 1)]
    public async Task MatchEmptyString(string source, string pattern, int init)
    {
        using var state = LuaState.Create();
        state.OpenStringLibrary();

        var result = await state.DoStringAsync(
            $"return string.match('{source}', '{pattern}', {init})"
        );

        Assert.That(result, Is.EqualTo(new LuaValue[] { "" }));
    }

    [TestCase("abc", 4)]
    [TestCase("", 1)]
    public async Task PositionCaptureAtEnd(string source, int init)
    {
        using var state = LuaState.Create();
        state.OpenStringLibrary();

        var result = await state.DoStringAsync(
            $"""
            local start, finish, position = string.find('{source}', '()', {init})
            return start, finish, position, string.match('{source}', '()', {init})
            """
        );

        Assert.That(result, Is.EqualTo(new LuaValue[] { init, init - 1, init, init }));
    }

    [TestCase("abc", "a+", 4)]
    [TestCase("abc", "a*", 5)]
    [TestCase("", "a+", 1)]
    [TestCase("", "^a", 1)]
    [TestCase("", "a*", 2)]
    public async Task RejectNonemptyMatchOrStartBeyondEnd(string source, string pattern, int init)
    {
        using var state = LuaState.Create();
        state.OpenStringLibrary();

        var result = await state.DoStringAsync(
            $"""
            return string.find('{source}', '{pattern}', {init}),
                   string.match('{source}', '{pattern}', {init})
            """
        );

        Assert.That(result, Is.EqualTo(new LuaValue[] { LuaValue.Nil, LuaValue.Nil }));
    }

    [TestCase("", "$", "", 1)]
    [TestCase("a", "$", "", 1)]
    [TestCase("", "a*", "", 1)]
    [TestCase("a", "a*", "a,", 2)]
    [TestCase("abc", "a*", "a,,,", 4)]
    [TestCase("", "()", "1", 1)]
    [TestCase("abc", "()", "1,2,3,4", 4)]
    [TestCase("abc", "", ",,,", 4)]
    public async Task GMatchIncludesEnd(string source, string pattern, string expected, int count)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();

        var result = await state.DoStringAsync(
            $$"""
            local source = '{{source}}'
            local matches = {}
            for value in string.gmatch(source, '{{pattern}}') do
                matches[#matches + 1] = value
                if #matches > #source + 1 then error('iterator did not terminate') end
            end
            return table.concat(matches, ','), #matches
            """
        );

        Assert.That(result, Is.EqualTo(new LuaValue[] { expected, count }));
    }

    [TestCase("abc", "$", 4, "abcX", 1)]
    [TestCase("", "$", 1, "X", 1)]
    [TestCase("", "^$", 1, "X", 1)]
    [TestCase("", "a*", 1, "X", 1)]
    [TestCase("abc", "()", 4, "XaXbXcX", 4)]
    [TestCase("a", "a*", 2, "XX", 2)]
    [TestCase("a", "a*", 1, "X", 1)]
    [TestCase("abc", "$", 0, "abc", 0)]
    [TestCase("", "a+", 1, "", 0)]
    [TestCase("abc", "", 4, "XaXbXcX", 4)]
    public async Task GSubIncludesEnd(
        string source,
        string pattern,
        int limit,
        string expected,
        int count
    )
    {
        using var state = LuaState.Create();
        state.OpenStringLibrary();

        var result = await state.DoStringAsync(
            $"return string.gsub('{source}', '{pattern}', 'X', {limit})"
        );

        Assert.That(result, Is.EqualTo(new LuaValue[] { expected, count }));
    }
}
