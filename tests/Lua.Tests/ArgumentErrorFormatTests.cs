using Lua.Standard;

namespace Lua.Tests;

/// <summary>
/// Lua 5.2 renders argument checks as "bad argument #1 to 'rep' (string expected, got boolean)";
/// the surrounding parentheses belong to the message wrapper and must appear exactly once.
/// </summary>
public class ArgumentErrorFormatTests
{
    [TestCase("return string.rep(true)", "bad argument #1 to 'rep' (string expected, got boolean)")]
    [TestCase("return table.concat(1)", "bad argument #1 to 'concat' (table expected, got number)")]
    [TestCase("return math.abs('x')", "bad argument #1 to 'abs' (number expected, got string)")]
    [TestCase(
        "return string.byte(false)",
        "bad argument #1 to 'byte' (string expected, got boolean)"
    )]
    public async Task TypeErrorsUseSingleParentheses(string source, string expected)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var exception = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.DoStringAsync(source, "=argument-format")
        );
        Assert.That(exception!.Message, Does.Contain(expected));
        Assert.That(exception.Message, Does.Not.Contain("))"));
    }

    [Test]
    public async Task MissingArgumentKeepsLuaWording()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var exception = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.DoStringAsync("return string.format('%d')")
        );
        Assert.That(exception!.Message, Does.Contain("bad argument #2 to 'format' (no value)"));
    }

    /// <summary>
    /// Ordering failures follow luaG_ordererror, which describes the values instead of naming the
    /// internal operator ("lt"); table.sort reports the same message for mixed element types.
    /// </summary>
    [TestCase("return 1 < 'a'", "attempt to compare number with string")]
    [TestCase("return {} < 1", "attempt to compare table with number")]
    [TestCase("return {} < {}", "attempt to compare two table values")]
    [TestCase(
        "local t = {1, 'a', 2} return table.sort(t)",
        "attempt to compare string with number"
    )]
    public async Task ComparisonErrorsDescribeTheValues(string source, string expected)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var exception = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.DoStringAsync(source, "=comparison")
        );
        Assert.That(exception!.Message, Does.Contain(expected));
        Assert.That(exception.Message, Does.Not.Contain("attempt to lt"));
    }

    [Test]
    public async Task PatternErrorsQuoteTheOffendingCharacter()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var exception = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.DoStringAsync("return ('x'):find('%')", "=pattern")
        );
        Assert.That(exception!.Message, Does.Contain("malformed pattern (ends with '%')"));
    }

    /// <summary>
    /// A trailing '%' makes Lua look for the value of the missing conversion, so the error names
    /// the absent argument rather than the (invisible) option character.
    /// </summary>
    [TestCase(
        "return pcall(string.format, 'abc%')",
        "bad argument #2 to 'string.format' (no value)"
    )]
    [TestCase(
        "return pcall(function() return string.format('abc%') end)",
        "bad argument #2 to 'format' (no value)"
    )]
    public async Task TrailingFormatConversionReportsTheMissingValue(string source, string expected)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var results = await state.DoStringAsync(source, "=format");
        Assert.That(results[0].Read<bool>(), Is.False);
        Assert.That(results[1].ToString(), Does.Contain(expected));
    }

    /// <summary>
    /// Lua's luaL_argerror falls back to the function's global path when the call site cannot name
    /// it (an indirect call through pcall), while a field call keeps the short name.
    /// </summary>
    [TestCase(
        "return pcall(string.byte, false)",
        "bad argument #1 to 'string.byte' (string expected, got boolean)"
    )]
    [TestCase(
        "return pcall(os.date, '!%Q')",
        "bad argument #1 to 'os.date' (invalid conversion specifier '%Q')"
    )]
    public async Task IndirectCallsUseTheGlobalFunctionName(string source, string expected)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var results = await state.DoStringAsync(source, "=names");
        Assert.That(results[0].Read<bool>(), Is.False);
        Assert.That(results[1].ToString(), Does.Contain(expected));
    }

    [Test]
    public async Task DirectCallsKeepTheShortFunctionName()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var exception = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.DoStringAsync("return string.byte(false)", "=names")
        );
        Assert.That(exception!.Message, Does.Contain("bad argument #1 to 'byte'"));
        Assert.That(exception.Message, Does.Not.Contain("string.byte"));
    }
}
