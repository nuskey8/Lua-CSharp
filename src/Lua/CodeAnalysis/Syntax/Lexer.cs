using System.Runtime.CompilerServices;
using Lua.Internal;

namespace Lua.CodeAnalysis.Syntax;

public ref struct Lexer
{
    public required ReadOnlyMemory<char> Source { get; init; }
    public string? ChunkName { get; init; }

    SyntaxToken current;
    SourcePosition position = new(1, 0);
    int offset;

    public Lexer()
    {
    }

    public readonly SyntaxToken Current => current;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Advance(int count)
    {
        var span = Source.Span;
        for (var i = 0; i < count; i++)
        {
            if (offset >= span.Length)
            {
                LuaParseException.SyntaxError(ChunkName, position, null);
            }

            var c = span[offset];
            offset++;

            var isLF = c is '\n';
            var isCR = c is '\r' && (span.Length == offset || span[offset] is not '\n');

            if (isLF || isCR)
            {
                position.Column = 0;
                position.Line++;
            }
            else
            {
                position.Column++;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool TryRead(int offset, out char value)
    {
        if (Source.Length <= offset)
        {
            value = default;
            return false;
        }

        value = Source.Span[offset];
        return true;
    }

    public bool MoveNext()
    {
        if (Source.Length <= offset)
        {
            return false;
        }

        var span = Source.Span;
        var startOffset = offset;
        var position = this.position;

        var c1 = span[offset];
        Advance(1);
        var c2 = span.Length == offset ? char.MinValue : span[offset];

        switch (c1)
        {
            case ' ':
            case '\t':
                while (offset < Source.Length && (Source.Span[offset] == ' ' || Source.Span[offset] == '\t'))
                {
                    Advance(1);
                }

                return MoveNext();
            case '\n':
                current = SyntaxToken.EndOfLine(position);
                return true;
            case '\r':
                if (c2 == '\n')
                {
                    Advance(1);
                }

                current = SyntaxToken.EndOfLine(position);
                return true;
            case '(':
                current = SyntaxToken.LParen(position);
                return true;
            case ')':
                current = SyntaxToken.RParen(position);
                return true;
            case '{':
                current = SyntaxToken.LCurly(position);
                return true;
            case '}':
                current = SyntaxToken.RCurly(position);
                return true;
            case ']':
                current = SyntaxToken.RSquare(position);
                return true;
            case '+':
                current = SyntaxToken.Addition(position);
                return true;
            case '-':
                // handle comments iteratively
                while (offset < span.Length && c2 == '-')
                {
                    var pos = position;
                    Advance(1); // consume first '-'

                    // block comment
                    if (span.Length > offset + 1 && span[offset] == '[' && (span[offset + 1] == '[' || span[offset + 1] == '='))
                    {
                        Advance(1); // consume second '-'
                        var (_, _, isTerminated) = ReadUntilLongBracketEnd(ref span);
                        if (!isTerminated)
                        {
                            LuaParseException.UnfinishedLongComment(ChunkName, pos);
                        }
                    }
                    else // line comment
                    {
                        ReadUntilEOL(ref span, ref offset, out _);
                    }

                    // prepare for next iteration
                    if (offset < span.Length)
                    {
                        c2 = (offset + 1 < span.Length) ? span[offset + 1] : char.MinValue;
                        if (span[offset] != '-') break; // next char is not a comment, exit loop
                    }
                    else
                    {
                        break;
                    }
                }

                // after skipping comments, if we reached end, return false
                if (offset >= span.Length)
                    return false;

                current = SyntaxToken.Subtraction(position); // if single '-' remains
                return true;
            case '*':
                current = SyntaxToken.Multiplication(position);
                return true;
            case '/':
                current = SyntaxToken.Division(position);
                return true;
            case '%':
                current = SyntaxToken.Modulo(position);
                return true;
            case '^':
                current = SyntaxToken.Exponentiation(position);
                return true;
            case '=':
                if (c2 == '=')
                {
                    current = SyntaxToken.Equality(position);
                    Advance(1);
                }
                else
                {
                    current = SyntaxToken.Assignment(position);
                }

                return true;
            case '~':
                if (c2 == '=')
                {
                    current = SyntaxToken.Inequality(position);
                    Advance(1);
                }
                else
                {
                    throw new LuaParseException(ChunkName, position, $"error: Invalid '~' token");
                }

                return true;
            case '>':
                if (c2 == '=')
                {
                    current = SyntaxToken.GreaterThanOrEqual(position);
                    Advance(1);
                }
                else
                {
                    current = SyntaxToken.GreaterThan(position);
                }

                return true;
            case '<':
                if (c2 == '=')
                {
                    current = SyntaxToken.LessThanOrEqual(position);
                    Advance(1);
                }
                else
                {
                    current = SyntaxToken.LessThan(position);
                }

                return true;
            case '.':
                if (c2 == '.')
                {
                    var c3 = span.Length == offset + 1 ? char.MinValue : span[offset + 1];

                    if (c3 == '.')
                    {
                        // vararg
                        current = SyntaxToken.VarArg(position);
                        Advance(2);
                    }
                    else
                    {
                        // concat
                        current = SyntaxToken.Concat(position);
                        Advance(1);
                    }

                    return true;
                }

                if (!StringHelper.IsNumber(c2))
                {
                    current = SyntaxToken.Dot(position);
                    return true;
                }

                break;
            case '#':
                current = SyntaxToken.Length(position);
                return true;
            case ',':
                current = SyntaxToken.Comma(position);
                return true;
            case ';':
                current = SyntaxToken.SemiColon(position);
                return true;
        }

        // numeric literal
        if (c1 is '.' || StringHelper.IsNumber(c1))
        {
            if (c1 is '0' && c2 is 'x' or 'X') // hex 0x
            {
                Advance(1);
                if (span[offset] is '.')
                {
                    Advance(1);
                }

                ReadDigit(ref span, ref offset, out var readCount);

                if (span.Length > offset && span[offset] is '.')
                {
                    Advance(1);
                    ReadDigit(ref span, ref offset, out _);
                }

                if (span.Length > offset && span[offset] is 'p' or 'P')
                {
                    Advance(1);
                    if (span[offset] is '-' or '+')
                    {
                        Advance(1);
                    }

                    ReadDigit(ref span, ref offset, out _);
                }

                if (readCount == 0)
                {
                    throw new LuaParseException(ChunkName, this.position, $"error: Illegal hexadecimal number");
                }
            }
            else
            {
                ReadNumber(ref span, ref offset, out _);

                if (span.Length > offset && span[offset] is '.')
                {
                    Advance(1);
                    ReadNumber(ref span, ref offset, out _);
                }

                if (span.Length > offset && span[offset] is 'e' or 'E')
                {
                    Advance(1);
                    if (span[offset] is '-' or '+')
                    {
                        Advance(1);
                    }

                    ReadNumber(ref span, ref offset, out _);
                }
            }

            current = new(SyntaxTokenType.Number, Source[startOffset..offset], position);
            return true;
        }

        // label
        if (c1 is ':')
        {
            if (c2 is ':')
            {
                var stringStartOffset = offset + 1;
                Advance(2);

                var prevC = char.MinValue;

                while (span.Length > offset)
                {
                    var c = span[offset];
                    if (prevC == ':' && c == ':')
                    {
                        break;
                    }

                    Advance(1);
                    prevC = c;
                }

                current = SyntaxToken.Label(Source[stringStartOffset..(offset - 1)], position);
                Advance(1);
            }
            else
            {
                current = SyntaxToken.Colon(position);
            }

            return true;
        }

        // short string literal
        if (c1 is '"' or '\'')
        {
            var quote = c1;
            var stringStartOffset = offset;
            var isTerminated = false;

            while (span.Length > offset)
            {
                var c = span[offset];

                if (c is '\n' or '\r')
                {
                    break;
                }

                if (c is '\\')
                {
                    Advance(1);

                    if (span.Length <= offset)
                    {
                        break;
                    }

                    if (span[offset] == '\r')
                    {
                        if (span.Length <= offset + 1)
                        {
                            continue;
                        }

                        if (span[offset + 1] == '\n')
                        {
                            Advance(1);
                        }
                    }
                }
                else if (c == quote)
                {
                    isTerminated = true;
                    break;
                }

                Advance(1);
            }

            if (!isTerminated)
            {
                throw new LuaParseException(ChunkName, this.position, "error: Unterminated string");
            }

            current = SyntaxToken.String(Source[stringStartOffset..offset], position);
            Advance(1);
            return true;
        }

        // long string literal
        if (c1 is '[')
        {
            if (c2 is '[' or '=')
            {
                var (start, end, isTerminated) = ReadUntilLongBracketEnd(ref span);

                if (!isTerminated)
                {
                    throw new LuaParseException(ChunkName, this.position, "error: Unterminated string");
                }

                current = SyntaxToken.RawString(Source[start..end], position);
                return true;
            }
            else
            {
                current = SyntaxToken.LSquare(position);
                return true;
            }
        }

        // identifier
        if (IsIdentifier(c1))
        {
            while (span.Length > offset && IsIdentifier(span[offset]))
            {
                Advance(1);
            }

            var identifier = Source[startOffset..offset];

            current = identifier.Span switch
            {
                Keywords.Nil => SyntaxToken.Nil(position),
                Keywords.True => SyntaxToken.True(position),
                Keywords.False => SyntaxToken.False(position),
                Keywords.And => SyntaxToken.And(position),
                Keywords.Or => SyntaxToken.Or(position),
                Keywords.Not => SyntaxToken.Not(position),
                Keywords.End => SyntaxToken.End(position),
                Keywords.Then => SyntaxToken.Then(position),
                Keywords.If => SyntaxToken.If(position),
                Keywords.ElseIf => SyntaxToken.ElseIf(position),
                Keywords.Else => SyntaxToken.Else(position),
                Keywords.Local => SyntaxToken.Local(position),
                Keywords.Return => SyntaxToken.Return(position),
                Keywords.Goto => SyntaxToken.Goto(position),
                Keywords.Do => SyntaxToken.Do(position),
                Keywords.In => SyntaxToken.In(position),
                Keywords.While => SyntaxToken.While(position),
                Keywords.Repeat => SyntaxToken.Repeat(position),
                Keywords.For => SyntaxToken.For(position),
                Keywords.Until => SyntaxToken.Until(position),
                Keywords.Break => SyntaxToken.Break(position),
                Keywords.Function => SyntaxToken.Function(position),
                _ => new(SyntaxTokenType.Identifier, identifier, position)
            };

            return true;
        }

        throw new LuaParseException(ChunkName, position, $"unexpected symbol near '{c1}'");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void ReadUntilEOL(ref ReadOnlySpan<char> span, ref int offset, out int readCount)
    {
        readCount = 0;
        var flag = true;
        while (flag)
        {
            if (span.Length <= offset)
            {
                return;
            }

            var c1 = span[offset];

            if (c1 is '\n')
            {
                flag = false;
            }
            else if (c1 is '\r')
            {
                var c2 = span.Length == offset + 1 ? char.MinValue : span[offset + 1];
                if (c2 is '\n')
                {
                    Advance(1);
                    readCount++;
                }

                flag = false;
            }

            Advance(1);
            readCount++;
        }
    }

    (int Start, int End, bool IsTerminated) ReadUntilLongBracketEnd(ref ReadOnlySpan<char> span)
    {
        var c = span[offset];
        var level = 0;
        while (c is '=')
        {
            level++;
            Advance(1);
            c = span[offset];
        }

        Advance(1);

        var startOffset = offset;
        var endOffset = 0;
        var isTerminated = false;
        var prevC = char.MinValue;

        while (span.Length > offset + level + 1)
        {
            var current = span[offset];

            // skip first newline
            if (offset == startOffset)
            {
                if (current == '\r')
                {
                    startOffset += 2;
                    Advance(span[offset + 1] == '\n' ? 2 : 1);
                    continue;
                }
                else if (current == '\n')
                {
                    startOffset++;
                    Advance(1);
                    continue;
                }
            }

            if (current is ']' && prevC is not '\\')
            {
                endOffset = offset;

                for (var i = 1; i <= level; i++)
                {
                    if (span[offset + i] is not '=')
                    {
                        goto CONTINUE;
                    }
                }

                if (span[offset + level + 1] is not ']')
                {
                    goto CONTINUE;
                }

                Advance(level + 2);
                isTerminated = true;
                break;
            }

        CONTINUE:
            prevC = current;
            Advance(1);
        }

        return (startOffset, endOffset, isTerminated);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void ReadDigit(ref ReadOnlySpan<char> span, ref int offset, out int readCount)
    {
        readCount = 0;
        while (span.Length > offset && StringHelper.IsDigit(span[offset]))
        {
            Advance(1);
            readCount++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void ReadNumber(ref ReadOnlySpan<char> span, ref int offset, out int readCount)
    {
        readCount = 0;
        while (span.Length > offset && StringHelper.IsNumber(span[offset]))
        {
            Advance(1);
            readCount++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool IsIdentifier(char c)
    {
        return c == '_' ||
               ('A' <= c && c <= 'Z') ||
               ('a' <= c && c <= 'z') ||
               StringHelper.IsNumber(c);
    }
}