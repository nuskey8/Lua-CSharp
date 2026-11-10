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
    [TestCase("return string.byte(false)", "bad argument #1 to 'byte' (string expected, got boolean)")]
    public async Task TypeErrorsUseSingleParentheses(string source, string expected)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var exception = Assert.ThrowsAsync<LuaRuntimeException>(
            async () => await state.DoStringAsync(source, "=argument-format")
        );
        Assert.That(exception!.Message, Does.Contain(expected));
        Assert.That(exception.Message, Does.Not.Contain("))"));
    }

    [Test]
    public async Task MissingArgumentKeepsLuaWording()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var exception = Assert.ThrowsAsync<LuaRuntimeException>(
            async () => await state.DoStringAsync("return string.format('%d')")
        );
        Assert.That(exception!.Message, Does.Contain("bad argument #2 to 'format' (no value)"));
    }
}
