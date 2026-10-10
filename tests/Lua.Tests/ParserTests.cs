using Lua.CodeAnalysis.Syntax;
using Lua.CodeAnalysis.Syntax.Nodes;

namespace Lua.Tests;

// TODO: add more tests

public class ParserTests
{
    [TestCase("if true print() end", "'then' expected near 'print'")]
    [TestCase("while true print() end", "'do' expected near 'print'")]
    [TestCase("for k, v pairs(t) do end", "'in' expected near 'pairs'")]
    [TestCase("function f a) end", "'(' expected near 'a'")]
    [TestCase("function f(a end", "')' expected near 'end'")]
    [TestCase("local t = {[1] 2}", "'=' expected near '2'")]
    [TestCase("local t = {[1 = 2}", "']' expected near '='")]
    [TestCase("local = 1", "<name> expected near '='")]
    [TestCase("local 123 = 1", "<name> expected near '123'")]
    [TestCase("local \"text\" = 1", "<name> expected near '\"text\"'")]
    [TestCase("local [[text]] = 1", "<name> expected near '[[text]]'")]
    [TestCase("if true then", "'end' expected near <eof>")]
    [TestCase("if true\n", "'then' expected near <eof>")]
    public void MissingToken_UsesLuaSpelling(string source, string expectedMessage)
    {
        var exception = Assert.Throws<LuaParseException>(() =>
            LuaSyntaxTree.Parse(source, "diagnostic.lua")
        )!;

        Assert.That(exception.Message, Does.EndWith(expectedMessage));
        Assert.That(exception.ChunkName, Is.EqualTo("diagnostic.lua"));
        Assert.That(exception.Position.Line, Is.EqualTo(1));
    }

    [TestCase(SyntaxTokenType.Then, "'then'")]
    [TestCase(SyntaxTokenType.ElseIf, "'elseif'")]
    [TestCase(SyntaxTokenType.GreaterThanOrEqual, "'>='")]
    [TestCase(SyntaxTokenType.Inequality, "'~='")]
    [TestCase(SyntaxTokenType.VarArg, "'...'")]
    [TestCase(SyntaxTokenType.Dot, "'.'")]
    [TestCase(SyntaxTokenType.Colon, "':'")]
    [TestCase(SyntaxTokenType.Identifier, "<name>")]
    [TestCase(SyntaxTokenType.Number, "<number>")]
    [TestCase(SyntaxTokenType.String, "<string>")]
    [TestCase(SyntaxTokenType.RawString, "<string>")]
    [TestCase(SyntaxTokenType.EndOfLine, "<eol>")]
    [SetCulture("tr-TR")]
    public void ExpectedToken_UsesSyntaxAndCultureIndependentCategoryNames(
        SyntaxTokenType token,
        string expectedText
    )
    {
        var position = new Lua.CodeAnalysis.SourcePosition(3, 4);
        var exception = Assert.Throws<LuaParseException>(() =>
            LuaParseException.ExpectedToken("diagnostic.lua", position, token)
        )!;

        Assert.That(exception.Message, Is.EqualTo($"diagnostic.lua:3: {expectedText} expected"));
        Assert.That(exception.Position, Is.EqualTo(position));
        Assert.That(exception.ChunkName, Is.EqualTo("diagnostic.lua"));
    }

    [TestCase("local", "syntax error near <eof>")]
    [TestCase("return t:", "syntax error near <eof>")]
    [TestCase("*a = 123", "unexpected symbol near '*'")]
    public void SyntaxErrors_ShowActualTokenOrEndOfInput(string source, string expectedMessage)
    {
        var exception = Assert.Throws<LuaParseException>(() =>
            LuaSyntaxTree.Parse(source, "diagnostic.lua")
        )!;
        Assert.That(exception.Message, Does.EndWith(expectedMessage));
    }

    [TestCase(SyntaxTokenType.Identifier, "myValue", "myValue")]
    [TestCase(SyntaxTokenType.Number, "1.5", "1.5")]
    [TestCase(SyntaxTokenType.String, "text", "\"text\"")]
    [TestCase(SyntaxTokenType.RawString, "text", "[[text]]")]
    public void TokenDisplay_PreservesLiteralText(
        SyntaxTokenType type,
        string text,
        string expectedText
    )
    {
        var token = new SyntaxToken(type, text.AsMemory(), default);
        Assert.That(token.ToDisplayString(), Is.EqualTo(expectedText));
    }

    [Test]
    public void Test_If_ElseIf_Else_Empty()
    {
        var source =
            @"if true then
elseif true then
else
end";
        var actual = LuaSyntaxTree.Parse(source).Nodes[0];
        var expected = new IfStatementNode(
            new()
            {
                Position = new(1, 8),
                ConditionNode = new BooleanLiteralNode(true, new(1, 3)),
                ThenNodes = [],
            },
            [
                new()
                {
                    Position = new(2, 13),
                    ConditionNode = new BooleanLiteralNode(true, new(2, 7)),
                    ThenNodes = [],
                },
            ],
            [],
            new(1, 0)
        );

        Assert.That(actual, Is.TypeOf<IfStatementNode>());
        Assert.That(actual.ToString(), Is.EqualTo(expected.ToString()));
    }
}
