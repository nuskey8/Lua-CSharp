namespace Lua.Tests;

public class ShebangTests
{
    [Test]
    public async Task Load_InitialHashbangLine_IsIgnored()
    {
        using var state = LuaState.Create();
        var closure = state.Load(
            """
            #!/usr/bin/env lua
            return 42
            """,
            "@shebang.lua");

        var result = await state.ExecuteAsync(closure);

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(42)));
    }

    [Test]
    public async Task Load_InitialHashCommentWithoutNewline_IsIgnored()
    {
        using var state = LuaState.Create();
        var closure = state.Load("# a non-ending comment", "@shebang.lua");

        var result = await state.ExecuteAsync(closure);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Load_InitialHashbangPreservesLineNumbers()
    {
        using var state = LuaState.Create();

        var exception = Assert.Throws<LuaCompileException>(() => state.Load(
            """
            #!/usr/bin/env lua
            return )
            """,
            "@shebang.lua"));

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.Position.Line, Is.EqualTo(2));
        Assert.That(exception.Message, Does.Contain("shebang.lua:2"));
    }

    [Test]
    public void Load_SourceStartingWithNul_Throws()
    {
        using var state = LuaState.Create();

        var exception = Assert.Throws<LuaCompileException>(() => state.Load("\0=1", "@nul.lua"));

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.Message, Does.Contain("unexpected symbol"));
    }

    [Test]
    public void Load_LeadingHashInNonFileChunk_IsNotIgnored()
    {
        using var state = LuaState.Create();

        var exception = Assert.Throws<LuaCompileException>(() => state.Load("#=1", "=expr"));

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.Message, Does.Contain("unexpected symbol"));
    }
}
