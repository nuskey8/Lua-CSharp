using Lua.Internal;

namespace Lua.CodeAnalysis.Syntax;

public readonly struct SyntaxToken(SyntaxTokenType type, ReadOnlyMemory<char> text, SourcePosition position) : IEquatable<SyntaxToken>
{
    public static SyntaxToken EndOfLine(SourcePosition position)
    {
        return new(SyntaxTokenType.EndOfLine, Keywords.LF.AsMemory(), position);
    }

    public static SyntaxToken LParen(SourcePosition position)
    {
        return new(SyntaxTokenType.LParen, Keywords.LParen.AsMemory(), position);
    }

    public static SyntaxToken RParen(SourcePosition position)
    {
        return new(SyntaxTokenType.RParen, Keywords.RParen.AsMemory(), position);
    }

    public static SyntaxToken LCurly(SourcePosition position)
    {
        return new(SyntaxTokenType.LCurly, Keywords.LCurly.AsMemory(), position);
    }

    public static SyntaxToken RCurly(SourcePosition position)
    {
        return new(SyntaxTokenType.RCurly, Keywords.RCurly.AsMemory(), position);
    }

    public static SyntaxToken LSquare(SourcePosition position)
    {
        return new(SyntaxTokenType.LSquare, Keywords.LSquare.AsMemory(), position);
    }

    public static SyntaxToken RSquare(SourcePosition position)
    {
        return new(SyntaxTokenType.RSquare, Keywords.RSquare.AsMemory(), position);
    }

    public static SyntaxToken Nil(SourcePosition position)
    {
        return new(SyntaxTokenType.Nil, Keywords.Nil.AsMemory(), position);
    }

    public static SyntaxToken True(SourcePosition position)
    {
        return new(SyntaxTokenType.True, Keywords.True.AsMemory(), position);
    }

    public static SyntaxToken False(SourcePosition position)
    {
        return new(SyntaxTokenType.False, Keywords.False.AsMemory(), position);
    }

    public static SyntaxToken Addition(SourcePosition position)
    {
        return new(SyntaxTokenType.Addition, Keywords.Addition.AsMemory(), position);
    }

    public static SyntaxToken Subtraction(SourcePosition position)
    {
        return new(SyntaxTokenType.Subtraction, Keywords.Subtraction.AsMemory(), position);
    }

    public static SyntaxToken Multiplication(SourcePosition position)
    {
        return new(SyntaxTokenType.Multiplication, Keywords.Multiplication.AsMemory(), position);
    }

    public static SyntaxToken Division(SourcePosition position)
    {
        return new(SyntaxTokenType.Division, Keywords.Division.AsMemory(), position);
    }

    public static SyntaxToken Modulo(SourcePosition position)
    {
        return new(SyntaxTokenType.Modulo, Keywords.Modulo.AsMemory(), position);
    }

    public static SyntaxToken Exponentiation(SourcePosition position)
    {
        return new(SyntaxTokenType.Exponentiation, Keywords.Exponentiation.AsMemory(), position);
    }

    public static SyntaxToken Equality(SourcePosition position)
    {
        return new(SyntaxTokenType.Equality, Keywords.Equality.AsMemory(), position);
    }

    public static SyntaxToken Inequality(SourcePosition position)
    {
        return new(SyntaxTokenType.Inequality, Keywords.Inequality.AsMemory(), position);
    }

    public static SyntaxToken GreaterThan(SourcePosition position)
    {
        return new(SyntaxTokenType.GreaterThan, Keywords.GreaterThan.AsMemory(), position);
    }

    public static SyntaxToken GreaterThanOrEqual(SourcePosition position)
    {
        return new(SyntaxTokenType.GreaterThanOrEqual, Keywords.GreaterThanOrEqual.AsMemory(), position);
    }

    public static SyntaxToken LessThan(SourcePosition position)
    {
        return new(SyntaxTokenType.LessThan, Keywords.LessThan.AsMemory(), position);
    }

    public static SyntaxToken LessThanOrEqual(SourcePosition position)
    {
        return new(SyntaxTokenType.LessThanOrEqual, Keywords.LessThanOrEqual.AsMemory(), position);
    }

    public static SyntaxToken Length(SourcePosition position)
    {
        return new(SyntaxTokenType.Length, Keywords.Length.AsMemory(), position);
    }

    public static SyntaxToken Concat(SourcePosition position)
    {
        return new(SyntaxTokenType.Concat, Keywords.Concat.AsMemory(), position);
    }

    public static SyntaxToken VarArg(SourcePosition position)
    {
        return new(SyntaxTokenType.VarArg, "...".AsMemory(), position);
    }

    public static SyntaxToken Assignment(SourcePosition position)
    {
        return new(SyntaxTokenType.Assignment, Keywords.Assignment.AsMemory(), position);
    }

    public static SyntaxToken And(SourcePosition position)
    {
        return new(SyntaxTokenType.And, Keywords.And.AsMemory(), position);
    }

    public static SyntaxToken Or(SourcePosition position)
    {
        return new(SyntaxTokenType.Or, Keywords.Or.AsMemory(), position);
    }

    public static SyntaxToken Not(SourcePosition position)
    {
        return new(SyntaxTokenType.Not, Keywords.Not.AsMemory(), position);
    }

    public static SyntaxToken End(SourcePosition position)
    {
        return new(SyntaxTokenType.End, Keywords.End.AsMemory(), position);
    }

    public static SyntaxToken Then(SourcePosition position)
    {
        return new(SyntaxTokenType.Then, Keywords.Then.AsMemory(), position);
    }

    public static SyntaxToken If(SourcePosition position)
    {
        return new(SyntaxTokenType.If, Keywords.If.AsMemory(), position);
    }

    public static SyntaxToken ElseIf(SourcePosition position)
    {
        return new(SyntaxTokenType.ElseIf, Keywords.ElseIf.AsMemory(), position);
    }

    public static SyntaxToken Else(SourcePosition position)
    {
        return new(SyntaxTokenType.Else, Keywords.Else.AsMemory(), position);
    }

    public static SyntaxToken Local(SourcePosition position)
    {
        return new(SyntaxTokenType.Local, Keywords.Local.AsMemory(), position);
    }

    public static SyntaxToken Return(SourcePosition position)
    {
        return new(SyntaxTokenType.Return, Keywords.Return.AsMemory(), position);
    }

    public static SyntaxToken Goto(SourcePosition position)
    {
        return new(SyntaxTokenType.Goto, Keywords.Goto.AsMemory(), position);
    }

    public static SyntaxToken Comma(SourcePosition position)
    {
        return new(SyntaxTokenType.Comma, ",".AsMemory(), position);
    }

    public static SyntaxToken Dot(SourcePosition position)
    {
        return new(SyntaxTokenType.Dot, ".".AsMemory(), position);
    }

    public static SyntaxToken SemiColon(SourcePosition position)
    {
        return new(SyntaxTokenType.SemiColon, ";".AsMemory(), position);
    }

    public static SyntaxToken Colon(SourcePosition position)
    {
        return new(SyntaxTokenType.Colon, ":".AsMemory(), position);
    }

    public static SyntaxToken Do(SourcePosition position)
    {
        return new(SyntaxTokenType.Do, Keywords.Do.AsMemory(), position);
    }

    public static SyntaxToken While(SourcePosition position)
    {
        return new(SyntaxTokenType.While, Keywords.While.AsMemory(), position);
    }

    public static SyntaxToken Repeat(SourcePosition position)
    {
        return new(SyntaxTokenType.Repeat, Keywords.Repeat.AsMemory(), position);
    }

    public static SyntaxToken Until(SourcePosition position)
    {
        return new(SyntaxTokenType.Until, Keywords.Until.AsMemory(), position);
    }

    public static SyntaxToken Break(SourcePosition position)
    {
        return new(SyntaxTokenType.Break, Keywords.Break.AsMemory(), position);
    }

    public static SyntaxToken Function(SourcePosition position)
    {
        return new(SyntaxTokenType.Function, Keywords.Function.AsMemory(), position);
    }

    public static SyntaxToken For(SourcePosition position)
    {
        return new(SyntaxTokenType.For, Keywords.For.AsMemory(), position);
    }

    public static SyntaxToken In(SourcePosition position)
    {
        return new(SyntaxTokenType.In, Keywords.In.AsMemory(), position);
    }

    public SyntaxTokenType Type { get; } = type;
    public ReadOnlyMemory<char> Text { get; } = text;
    public SourcePosition Position { get; } = position;

    public static SyntaxToken Number(string text, SourcePosition position)
    {
        return new(SyntaxTokenType.Number, text.AsMemory(), position);
    }

    public static SyntaxToken Number(ReadOnlyMemory<char> text, SourcePosition position)
    {
        return new(SyntaxTokenType.Number, text, position);
    }

    public static SyntaxToken Identifier(string text, SourcePosition position)
    {
        return new(SyntaxTokenType.Identifier, text.AsMemory(), position);
    }

    public static SyntaxToken Identifier(ReadOnlyMemory<char> text, SourcePosition position)
    {
        return new(SyntaxTokenType.Identifier, text, position);
    }

    public static SyntaxToken String(ReadOnlyMemory<char> text, SourcePosition position)
    {
        return new(SyntaxTokenType.String, text, position);
    }

    public static SyntaxToken RawString(ReadOnlyMemory<char> text, SourcePosition position)
    {
        return new(SyntaxTokenType.RawString, text, position);
    }

    public static SyntaxToken Label(ReadOnlyMemory<char> text, SourcePosition position)
    {
        return new(SyntaxTokenType.Label, text, position);
    }

    public override string ToString()
    {
        return $"{Position} {Type}:{Text}";
    }

    public string ToDisplayString()
    {
        return Type switch
        {
            SyntaxTokenType.EndOfLine => Keywords.LF,
            SyntaxTokenType.LParen => Keywords.LParen,
            SyntaxTokenType.RParen => Keywords.RParen,
            SyntaxTokenType.LCurly => Keywords.LCurly,
            SyntaxTokenType.RCurly => Keywords.RCurly,
            SyntaxTokenType.LSquare => Keywords.LSquare,
            SyntaxTokenType.RSquare => Keywords.RSquare,
            SyntaxTokenType.SemiColon => ";",
            SyntaxTokenType.Comma => ",",
            SyntaxTokenType.Number => Text.ToString(),
            SyntaxTokenType.String => $"\"{Text}\"",
            SyntaxTokenType.RawString => $"[[{Text}]]",
            SyntaxTokenType.Nil => Keywords.Nil,
            SyntaxTokenType.True => Keywords.True,
            SyntaxTokenType.False => Keywords.False,
            SyntaxTokenType.Identifier => Text.ToString(),
            SyntaxTokenType.Addition => Keywords.Addition,
            SyntaxTokenType.Subtraction => Keywords.Subtraction,
            SyntaxTokenType.Multiplication => Keywords.Multiplication,
            SyntaxTokenType.Division => Keywords.Division,
            SyntaxTokenType.Modulo => Keywords.Modulo,
            SyntaxTokenType.Exponentiation => Keywords.Exponentiation,
            SyntaxTokenType.Equality => Keywords.Equality,
            SyntaxTokenType.Inequality => Keywords.Inequality,
            SyntaxTokenType.GreaterThan => Keywords.GreaterThan,
            SyntaxTokenType.LessThan => Keywords.LessThan,
            SyntaxTokenType.GreaterThanOrEqual => Keywords.GreaterThanOrEqual,
            SyntaxTokenType.LessThanOrEqual => Keywords.LessThanOrEqual,
            SyntaxTokenType.And => Keywords.And,
            SyntaxTokenType.Not => Keywords.Not,
            SyntaxTokenType.Or => Keywords.Or,
            SyntaxTokenType.Assignment => Keywords.Assignment,
            SyntaxTokenType.Concat => Keywords.Concat,
            SyntaxTokenType.Length => Keywords.Length,
            SyntaxTokenType.Break => Keywords.Break,
            SyntaxTokenType.Do => Keywords.Do,
            SyntaxTokenType.For => Keywords.For,
            SyntaxTokenType.Goto => Keywords.Goto,
            SyntaxTokenType.If => Keywords.If,
            SyntaxTokenType.ElseIf => Keywords.ElseIf,
            SyntaxTokenType.Else => Keywords.Else,
            SyntaxTokenType.Function => Keywords.Function,
            SyntaxTokenType.End => Keywords.End,
            SyntaxTokenType.Then => Keywords.Then,
            SyntaxTokenType.In => Keywords.In,
            SyntaxTokenType.Local => Keywords.Local,
            SyntaxTokenType.Repeat => Keywords.Repeat,
            SyntaxTokenType.Return => Keywords.Return,
            SyntaxTokenType.Until => Keywords.Until,
            SyntaxTokenType.While => Keywords.While,
            _ => ""
        };
    }

    public bool Equals(SyntaxToken other)
    {
        return other.Type == Type &&
               other.Text.Span.SequenceEqual(Text.Span) &&
               other.Position == Position;
    }

    public override bool Equals(object? obj)
    {
        if (obj is SyntaxToken token)
        {
            return Equals(token);
        }

        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Type, Utf16StringMemoryComparer.Default.GetHashCode(Text), Position);
    }

    public static bool operator ==(SyntaxToken left, SyntaxToken right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(SyntaxToken left, SyntaxToken right)
    {
        return !(left == right);
    }
}

public enum SyntaxTokenType
{
    /// <summary>
    /// Invalid token
    /// </summary>
    Invalid,

    /// <summary>
    /// End of line
    /// </summary>
    EndOfLine,

    /// <summary>
    /// Left parenthesis '('
    /// </summary>
    LParen,

    /// <summary>
    /// Right parenthesis ')'
    /// </summary>
    RParen,

    /// <summary>
    /// Left curly bracket '{'
    /// </summary>
    LCurly,

    /// <summary>
    /// Right curly bracket '}'
    /// </summary>
    RCurly,

    /// <summary>
    /// Left square bracket '['
    /// </summary>
    LSquare,

    /// <summary>
    /// Right square bracket ']'
    /// </summary>
    RSquare,

    /// <summary>
    /// Semi colon (;)
    /// </summary>
    SemiColon,

    /// <summary>
    /// Colon (:)
    /// </summary>
    Colon,

    /// <summary>
    /// Comma (,)
    /// </summary>
    Comma,

    /// <summary>
    /// Dot (.)
    /// </summary>
    Dot,

    /// <summary>
    /// Numeric literal (e.g. 1, 2, 1.0, 2.0, ...)
    /// </summary>
    Number,

    /// <summary>
    /// String literal (e.g. "foo", "bar", ...)
    /// </summary>
    String,

    /// <summary>
    /// Raw string literal (e.g. [[Hello, World!]])
    /// </summary>
    RawString,

    /// <summary>
    /// Nil literal (nil)
    /// </summary>
    Nil,

    /// <summary>
    /// Boolean literal (true)
    /// </summary>
    True,

    /// <summary>
    /// Boolean literal (false)
    /// </summary>
    False,

    /// <summary>
    /// Identifier
    /// </summary>
    Identifier,

    /// <summary>
    /// Label
    /// </summary>
    Label,

    /// <summary>
    /// Addition operator (+)
    /// </summary>
    Addition,

    /// <summary>
    /// Subtraction operator (-)
    /// </summary>
    Subtraction,

    /// <summary>
    /// Multiplication operator (*)
    /// </summary>
    Multiplication,

    /// <summary>
    /// Division operator (/)
    /// </summary>
    Division,

    /// <summary>
    /// Modulo operator (%)
    /// </summary>
    Modulo,

    /// <summary>
    /// Exponentiation operator (^)
    /// </summary>
    Exponentiation,

    Equality, // ==
    Inequality, // ~=
    GreaterThan, // >
    LessThan, // <
    GreaterThanOrEqual, // >=
    LessThanOrEqual, // <=

    And, // and
    Not, // not
    Or, // or

    /// <summary>
    /// Assignment operator (=)
    /// </summary>
    Assignment,

    Concat, // ..
    Length, // #

    VarArg, // ...

    Break, // break
    Do, // do
    For, // for
    Goto, // goto

    If, // if
    ElseIf, // elseif
    Else, // else
    Function, // function

    End, // end
    Then, // then

    In, // in
    Local, // local
    Repeat, // repeat
    Return, // return
    Until, // until
    While // while
}