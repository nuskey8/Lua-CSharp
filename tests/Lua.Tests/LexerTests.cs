using Lua.CodeAnalysis.Syntax;

namespace Lua.Tests;

public class LexerTests
{
    [Test]
    [TestCase("0")]
    [TestCase("123")]
    [TestCase("1234567890")]
    public void Test_Numeric_Integer(string x)
    {
        var expected = new[]
        {
            SyntaxToken.Number(x, new(1, 0))
        };
        var actual = GetTokens(x);

        CollectionAssert.AreEqual(expected, actual);
    }

    [Test]
    [TestCase("1.2")]
    [TestCase("123.45")]
    [TestCase("12345.6789")]
    public void Test_Numeric_Decimal(string x)
    {
        var expected = new[]
        {
            SyntaxToken.Number(x, new(1, 0))
        };
        var actual = GetTokens(x);

        CollectionAssert.AreEqual(expected, actual);
    }

    [Test]
    [TestCase("0x123")]
    [TestCase("0x456")]
    [TestCase("0x789")]
    public void Test_Numeric_Hex(string x)
    {
        var expected = new[]
        {
            SyntaxToken.Number(x, new(1, 0))
        };
        var actual = GetTokens(x);

        CollectionAssert.AreEqual(expected, actual);
    }

    [Test]
    [TestCase("12E3")]
    [TestCase("45E+6")]
    [TestCase("78E-9")]
    [TestCase("1e+2")]
    [TestCase("3e-4")]
    public void Test_Numeric_Exponential(string x)
    {
        var expected = new[]
        {
            SyntaxToken.Number(x, new(1, 0))
        };
        var actual = GetTokens(x);

        CollectionAssert.AreEqual(expected, actual);
    }

    [Test]
    [TestCase("\"\"")]
    [TestCase("\"hello\"")]
    [TestCase("\"1.23\"")]
    [TestCase("\"1-2-3-4-5\"")]
    [TestCase("\'hello\'")]
    public void Test_String(string x)
    {
        var expected = new[]
        {
            SyntaxToken.String(x.AsMemory(1, x.Length - 2), new(1, 0))
        };
        var actual = GetTokens(x);

        CollectionAssert.AreEqual(expected, actual);
    }

    [Test]
    [TestCase("foo")]
    [TestCase("bar")]
    [TestCase("baz")]
    public void Test_Identifier(string x)
    {
        var expected = new[]
        {
            SyntaxToken.Identifier(x, new(1, 0))
        };
        var actual = GetTokens(x);

        CollectionAssert.AreEqual(expected, actual);
    }

    [Test]
    [TestCase("-- hello!")]
    [TestCase("-- how are you?")]
    [TestCase("-- goodbye!")]
    public void Test_Comment_Line(string code)
    {
        var expected = Array.Empty<SyntaxToken>();
        var actual = GetTokens(code);

        CollectionAssert.AreEqual(expected, actual);
    }

    [Test]
    [TestCase(@"--[[
        hello!
        how are you?
        goodbye!
    ]]--")]
    [TestCase(@"--[[
        hello!
        how are you?
        goodbye!
    ]]")]
    public void Test_Comment_Block(string code)
    {
        var expected = Array.Empty<SyntaxToken>();
        var actual = GetTokens(code);

        CollectionAssert.AreEqual(expected, actual);
    }

    [Test]
    [TestCase("--[[ \n hello")]
    [TestCase("--[[ \r hello")]
    [TestCase("--[[ \r\n hello")]
    public void Test_Comment_Block_Error(string code)
    {
        Assert.Throws<LuaParseException>(() => GetTokens(code), "main.lua:(1,5): unfinished long comment (starting at line 0)");
    }

    [Test]
    public void Test_Comment_Line_WithCode()
    {
        var expected = new[]
        {
            SyntaxToken.Number("10.0", new(1, 0))
        };
        var actual = GetTokens("10.0 -- this is numeric literal");

        CollectionAssert.AreEqual(expected, actual);
    }

    [Test]
    public void Test_Nil()
    {
        var expected = new[]
        {
            SyntaxToken.Nil(new(1, 0))
        };
        var actual = GetTokens("nil");

        CollectionAssert.AreEqual(expected, actual);
    }

    [Test]
    public void Test_True()
    {
        var expected = new[]
        {
            SyntaxToken.True(new(1, 0))
        };
        var actual = GetTokens("true");

        CollectionAssert.AreEqual(expected, actual);
    }

    [Test]
    public void Test_False()
    {
        var expected = new[]
        {
            SyntaxToken.False(new(1, 0))
        };
        var actual = GetTokens("false");

        CollectionAssert.AreEqual(expected, actual);
    }

    [Test]
    public void Test_If()
    {
        var expected = new[]
        {
            SyntaxToken.If(new(1, 0)), SyntaxToken.Identifier("x", new(1, 3)), SyntaxToken.Equality(new(1, 5)), SyntaxToken.Number("1.0", new(1, 8)), SyntaxToken.Then(new(1, 12)), SyntaxToken.EndOfLine(new(1, 16)),
            SyntaxToken.Return(new(2, 4)), SyntaxToken.Nil(new(2, 11)), SyntaxToken.EndOfLine(new(2, 14)),
            SyntaxToken.End(new(3, 0)),
        };
        var actual = GetTokens(
@"if x == 1.0 then
    return nil
end");

        CollectionAssert.AreEqual(expected, actual);
    }

    [Test]
    public void Test_If_Else()
    {
        var expected = new[]
        {
            SyntaxToken.If(new(1, 0)), SyntaxToken.Identifier("x", new(1, 3)), SyntaxToken.Equality(new(1, 5)), SyntaxToken.Number("1.0", new(1, 8)), SyntaxToken.Then(new(1, 12)), SyntaxToken.EndOfLine(new(1, 16)),
            SyntaxToken.Return(new(2, 4)), SyntaxToken.Number("1.0", new(2, 11)), SyntaxToken.EndOfLine(new(2, 14)),
            SyntaxToken.Else(new(3, 0)), SyntaxToken.EndOfLine(new(3, 4)),
            SyntaxToken.Return(new(4, 4)), SyntaxToken.Number("0.0", new(4, 11)), SyntaxToken.EndOfLine(new(4, 14)),
            SyntaxToken.End(new(5, 0)),
        };
        var actual = GetTokens(
@"if x == 1.0 then
    return 1.0
else
    return 0.0
end");

        CollectionAssert.AreEqual(expected, actual);
    }

    static SyntaxToken[] GetTokens(string source)
    {
        var list = new List<SyntaxToken>();
        var lexer = new Lexer
        {
            Source = source.AsMemory(),
            ChunkName = "main.lua"
        };
        while (lexer.MoveNext())
        {
            list.Add(lexer.Current);
        }
        return list.ToArray();
    }
}