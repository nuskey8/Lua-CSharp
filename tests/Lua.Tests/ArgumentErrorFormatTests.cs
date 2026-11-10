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
}
