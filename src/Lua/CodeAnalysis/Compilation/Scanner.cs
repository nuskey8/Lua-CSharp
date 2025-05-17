using Lua.Internal;
using System.Globalization;
using System.Text;
using static System.Diagnostics.Debug;

namespace Lua.CodeAnalysis.Compilation;

using static Constants;

internal struct Scanner
{
    public LuaState L;
    public PooledList<char> Buffer;
    public TextReader R;
    public int Current;
    public int LineNumber, LastLine;
    public string Source;
    public Token LookAheadToken;
    private int lastNewLinePos;

    ///inline
    public Token Token;

    public int T => Token.T;

    public const int FirstReserved = ushort.MaxValue + 257;
    public const int EndOfStream = -1;

    public const int MaxInt = int.MaxValue >> 1 + 1; //9223372036854775807

    public const int TkAnd = FirstReserved;
    public const int TkBreak = TkAnd + 1;
    public const int TkDo = TkBreak + 1;
    public const int TkElse = TkDo + 1;
    public const int TkElseif = TkElse + 1;
    public const int TkEnd = TkElseif + 1;
    public const int TkFalse = TkEnd + 1;
    public const int TkFor = TkFalse + 1;
    public const int TkFunction = TkFor + 1;
    public const int TkGoto = TkFunction + 1;
    public const int TkIf = TkGoto + 1;
    public const int TkIn = TkIf + 1;
    public const int TkLocal = TkIn + 1;
    public const int TkNil = TkLocal + 1;
    public const int TkNot = TkNil + 1;
    public const int TkOr = TkNot + 1;
    public const int TkRepeat = TkOr + 1;
    public const int TkReturn = TkRepeat + 1;
    public const int TkThen = TkReturn + 1;
    public const int TkTrue = TkThen + 1;
    public const int TkUntil = TkTrue + 1;
    public const int TkWhile = TkUntil + 1;
    public const int TkConcat = TkWhile + 1;
    public const int TkDots = TkConcat + 1;
    public const int TkEq = TkDots + 1;
    public const int TkGe = TkEq + 1;
    public const int TkLe = TkGe + 1;
    public const int TkNe = TkLe + 1;
    public const int TkDoubleColon = TkNe + 1;
    public const int TkEos = TkDoubleColon + 1;
    public const int TkNumber = TkEos + 1;
    public const int TkName = TkNumber + 1;
    public const int TkString = TkName + 1;

    public const int ReservedCount = TkWhile - FirstReserved + 1;

    static readonly string[] tokens =
    [
        "and", "break", "do", "else", "elseif",
        "end", "false", "for", "function", "goto", "if",
        "in", "local", "nil", "not", "or", "repeat",
        "return", "then", "true", "until", "while",
        "..", "...", "==", ">=", "<=", "~=", "::", "<eof>",
        "<number>", "<name>", "<string>"
    ];

    public static ReadOnlySpan<string> Tokens => tokens;
    public void SyntaxError(int position, string message) => ScanError(position, message, Token.T);
    public void SyntaxError(string message) => ScanError(R.Position, message, Token.T);
    public void ErrorExpected(int position, char t) => SyntaxError(position, TokenToString(t) + " expected");

    public void NumberError(int numberStartPosition, int position)
    {
        Buffer.Clear();
        Token = new Token(numberStartPosition, TkString, R.Span[numberStartPosition..(position - 1)].ToString());
        ScanError(position, "malformed number", TkString);
    }

    public static bool IsNewLine(int c) => c is '\n' or '\r';

    public static bool IsDecimal(int c) => c is >= '0' and <= '9';


    public static string TokenToString(Token t) => t.T switch
    {
        TkName or TkString => t.S,
        TkNumber => $"{t.N}",
        < FirstReserved => $"{(char)t.T}", // TODO check for printable rune
        < TkEos => $"'{tokens[t.T - FirstReserved]}'",
        _ => tokens[t.T - FirstReserved]
    };

    public string TokenToString(int t) => t switch
    {
        TkName or TkString => Token.S,
        TkNumber => $"{Token.N}",
        < FirstReserved => $"{(char)t}", // TODO check for printable rune
        < TkEos => $"'{tokens[t - FirstReserved]}'",
        _ => tokens[t - FirstReserved]
    };

    public static string TokenRuteToString(int t) => t switch
    {
        < FirstReserved => $"{(char)t}", // TODO check for printable rune
        <= TkString => $"'{tokens[t - FirstReserved]}'",
        _ => tokens[t - FirstReserved]
    };

    public void ScanError(int pos, string message, int token)
    {
        var shortSourceBuffer = (stackalloc char[59]);
        var len = LuaDebug.WriteShortSource(Source, shortSourceBuffer);
        var buff = shortSourceBuffer[..len].ToString();
        string? nearToken = null;
        if (token != 0)
        {
            nearToken = TokenToString(token);
        }

        throw new LuaCompileException(buff, new SourcePosition(LineNumber, pos - lastNewLinePos + 1), pos - 1, message, nearToken);
    }

    public void IncrementLineNumber()
    {
        var old = Current;
        Assert(IsNewLine(old));
        Advance();
        if (IsNewLine(Current) && Current != old) Advance();
        lastNewLinePos = R.Position;
        if (++LineNumber >= MaxLine) SyntaxError(lastNewLinePos, "chunk has too many lines");
    }

    public void Advance()
    {
        Current = R.TryRead(out var c) ? c : EndOfStream;
    }

    public void SaveAndAdvance()
    {
        Save(Current);
        Advance();
    }

    public void AdvanceAndSave(int c)
    {
        Advance();
        Save(c);
    }

    public void Save(int c)
    {
        Buffer.Add((char)c);
    }

    public bool CheckNext(string str)
    {
        if (Current == 0 || !str.Contains((char)Current)) return false;
        SaveAndAdvance();
        return true;
    }

    public int SkipSeparator()
    {
        var (i, c) = (0, Current);
        Assert(c is '[' or ']');
        for (SaveAndAdvance(); Current == '='; i++) SaveAndAdvance();
        if (Current == c) return i;
        return -i - 1;
    }

    public string ReadMultiLine(bool comment, int sep)
    {
        SaveAndAdvance();
        if (IsNewLine(Current))
        {
            IncrementLineNumber();
        }

        for (;;)
        {
            switch (Current)
            {
                case EndOfStream:
                    ScanError(R.Position, comment ? "unfinished long comment" : "unfinished long string", TkEos);

                    break;
                case ']':
                    if (SkipSeparator() == sep)
                    {
                        SaveAndAdvance();
                        if (!comment)
                        {
                            var s = Buffer.AsSpan().Slice(2 + sep, Buffer.Length - (4 + 2 * sep)).ToString();
                            Buffer.Clear();
                            return s;
                        }

                        Buffer.Clear();
                        return "";
                    }

                    break;
                case '\r':
                    goto case '\n';
                case '\n':
                    Save('\n');
                    IncrementLineNumber();
                    break;
                default:
                    if (!comment)
                    {
                        Save(Current);
                    }

                    Advance();
                    break;
            }
        }
    }

    public int ReadDigits()
    {
        var c = Current;
        for (; IsDecimal(c); c = Current) SaveAndAdvance();
        return c;
    }

    public static bool IsHexadecimal(int c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    public (double n, int c, int i) ReadHexNumber(double x, ref int position)
    {
        var c = Current;
        var n = x;
        if (!IsHexadecimal(c))
        {
            return (n, c, 0);
        }

        position++;
        var i = 0;
        for (;;)
        {
            switch (c)
            {
                case >= '0' and <= '9':
                    c = c - '0';
                    break;
                case >= 'a' and <= 'f':
                    c = c - 'a' + 10;
                    break;
                case >= 'A' and <= 'F':
                    c = c - 'A' + 10;
                    break;
                case EndOfStream or '}' or ',' or '.' or ')' or 'p' or 'P': return (n, c, i);
                default:
                    if (IsWhiteSpace(c)) return (n, c, i);
                    return (n, 0, 0);
            }

            Advance();
            position++;
            (c, n, i) = (Current, n * 16.0 + c, i + 1);
        }
    }

    public Token ReadNumber(int pos)
    {
        var startPosition = pos - 1;
        var c = Current;
        Assert(IsDecimal(c));
        SaveAndAdvance();
        if (c == '0' && CheckNext("Xx")) // hexadecimal
        {
            pos++;
            Buffer.Clear();
            var exponent = 0;
            (var fraction, c, var i) = ReadHexNumber(0, ref pos);
            if (c == '.')
            {
                Advance();
                (fraction, c, exponent) = ReadHexNumber(fraction, ref pos);
            }

            if (i == 0 && exponent == 0)
            {
                NumberError(startPosition, pos);
            }

            exponent *= -4;
            if (c is 'p' or 'P')
            {
                Advance();
                var negativeExponent = false;
                c = Current;
                if (c is '+' or '-')
                {
                    negativeExponent = c == '-';
                    Advance();
                }

                if (!IsDecimal(Current))
                {
                    NumberError(startPosition, pos + 1);
                }

                _ = ReadDigits();

                if (!long.TryParse(Buffer.AsSpan(), NumberStyles.Float, CultureInfo.InvariantCulture, out long e))
                {
                    NumberError(startPosition, pos + 1);
                }
                else if (negativeExponent)
                {
                    exponent += (int)(-e);
                }
                else
                {
                    exponent += (int)e;
                }

                Buffer.Clear();
            }

            return new(pos, fraction * Math.Pow(2, exponent));
        }

        c = ReadDigits();
        if (c == '.')
        {
            SaveAndAdvance();
            c = ReadDigits();
        }

        if (c is 'e' or 'E')
        {
            SaveAndAdvance();
            c = Current;
            if (c is '+' or '-')
            {
                SaveAndAdvance();
            }

            _ = ReadDigits();
        }

        var strSpan = Buffer.AsSpan();
        if (strSpan.StartsWith("0"))
        {
            if (strSpan.Length == 1)
            {
                Buffer.Clear();
                return new(pos, 0d);
            }

            while (strSpan.Length > 1 && strSpan[0] == '0' && strSpan[1] == '0')
            {
                strSpan = strSpan[1..];
            }
        }

        if (!double.TryParse(strSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out double f))
        {
            NumberError(startPosition, pos);
        }

        Buffer.Clear();
        return new(pos, f);
    }

    static readonly Dictionary<int, char> escapes = new()
    {
        { 'a', '\a' },
        { 'b', '\b' },
        { 'f', '\f' },
        { 'n', '\n' },
        { 'r', '\r' },
        { 't', '\t' },
        { 'v', '\v' },
        { '\\', '\\' },
        { '"', '"' },
        { '\'', '\'' },
    };

    public void EscapeError(int pos, ReadOnlySpan<int> c, string message)
    {
        Buffer.Clear();
        Save('\'');
        Save('\\');
        foreach (var r in c)
        {
            if (r == EndOfStream)
            {
                break;
            }

            Save(r);
        }

        Save('\'');

        Token = new(pos - Buffer.Length, TkString, Buffer.AsSpan().ToString());
        Buffer.Clear();
        ScanError(pos, message, TkString);
    }

    public int ReadHexEscape()
    {
        Advance();
        var r = 0;
        var b = (stackalloc int[3] { 'x', 0, 0 });
        var (i, c) = (1, Current);
        for (; i < b.Length; (i, c, r) = (i + 1, Current, (r << 4) + c))
        {
            b[i] = c;
            switch (c)
            {
                case >= '0' and <= '9':
                    c -= '0';
                    break;
                case >= 'a' and <= 'f':
                    c -= ('a' - 10);
                    break;
                case >= 'A' and <= 'F':
                    c -= ('A' - 10);
                    break;
                default:
                    EscapeError(R.Position - 1, b.Slice(0, i + 1), "hexadecimal digit expected");
                    break;
            }

            Advance();
        }

        return r;
    }

    public int ReadDecimalEscape()
    {
        var b = (stackalloc int[3] { 0, 0, 0 });
        var c = Current;
        var r = 0;
        var pos = R.Position;
        for (int i = 0; i < b.Length && IsDecimal(c); i++, c = Current)
        {
            b[i] = c;
            r = 10 * r + c - '0';
            Advance();
            pos = R.Position;
        }

        if (r > 255)
        {
            EscapeError(pos - 1, b, "decimal escape too large");
        }

        return r;
    }

    public Token ReadString()
    {
        var pos = R.Position;
        var delimiter = Current;
        for (SaveAndAdvance(); Current != delimiter;)
        {
            switch (Current)
            {
                case EndOfStream:
                    Token = new(R.Position - Buffer.Length, TkString, Buffer.AsSpan().ToString());
                    ScanError(R.Position, "unfinished string", TkEos);
                    break;
                case '\n' or '\r':
                    Token = new(R.Position - Buffer.Length, TkString, Buffer.AsSpan().ToString());
                    ScanError(R.Position, "unfinished string", TkString);
                    break;
                case '\\':
                    Advance();
                    var c = Current;
                    if (escapes.TryGetValue(c, out var esc))
                    {
                        AdvanceAndSave(esc);
                    }
                    else if (IsNewLine(c))
                    {
                        IncrementLineNumber();
                        Save('\n');
                    }
                    else if (c == EndOfStream) // do nothing
                    {
                    }
                    else if (c == 'x')
                    {
                        Save(ReadHexEscape());
                    }
                    else if (c == 'z')
                    {
                        for (Advance(); IsWhiteSpace(Current);)
                        {
                            if (IsNewLine(Current))
                            {
                                IncrementLineNumber();
                            }
                            else
                            {
                                Advance();
                            }
                        }
                    }
                    else if (IsDecimal(c))
                    {
                        Save(ReadDecimalEscape());
                    }
                    else
                    {
                        EscapeError(R.Position - 1, [c], "invalid escape sequence");
                    }

                    break;
                default:
                    SaveAndAdvance();
                    break;
            }
        }

        SaveAndAdvance();
        var length = Buffer.Length - 2;
        // if (0<length&&Buffer[^2] == '\0')
        // {
        //     length--;
        // }
        var str = Buffer.AsSpan().Slice(1, length).ToString();
        Buffer.Clear();
        return new(pos, TkString, str);
    }

    public static bool IsReserved(string s)
    {
        foreach (var reserved in Tokens)
        {
            if (s == reserved)
            {
                return true;
            }
        }

        return false;
    }

    public Token ReservedOrName()
    {
        var pos = R.Position - Buffer.Length;
        var str = Buffer.AsSpan().ToString();
        Buffer.Clear();
        for (var i = 0; i < Tokens.Length; i++)
        {
            if (str == Tokens[i])
            {
                return new(pos, (i + FirstReserved), str);
            }
        }

        return new(pos, TkName, str);
    }

    public Token Scan()
    {
        const bool comment = true, str = false;
        var pos = R.Position;
        while (true)
        {
            var c = Current;
            switch (c)
            {
                case '\n':
                case '\r':
                    IncrementLineNumber();
                    break;
                case ' ':
                case '\f':
                case '\t':
                case '\v':
                    Advance();
                    pos = R.Position;
                    break;
                case '-':
                    Advance();
                    if (Current != '-')
                    {
                        return new(pos, '-');
                    }

                    Advance();
                    if (Current == '[')
                    {
                        var sep = SkipSeparator();
                        if (sep >= 0)
                        {
                            _ = ReadMultiLine(comment, sep);
                            break;
                        }

                        Buffer.Clear();
                    }


                    while (!IsNewLine(Current) && (Current != EndOfStream))
                    {
                        Advance();
                    }

                    break;
                case '[':
                    {
                        var sep = SkipSeparator();
                        if (sep >= 0)
                        {
                            return new(pos, TkString, ReadMultiLine(str, sep));
                        }

                        Buffer.Clear();
                        if (sep == -1) return new(pos, '[');

                        ScanError(pos, "invalid long string delimiter", TkString);
                        break;
                    }
                case '=':
                    Advance();
                    if (Current != '=')
                    {
                        return new(pos, '=');
                    }

                    Advance();
                    return new(pos, TkEq);
                case '<':
                    Advance();
                    if (Current != '=')
                    {
                        return new(pos, '<');
                    }

                    Advance();
                    return new(pos, TkLe);
                case '>':
                    Advance();
                    if (Current != '=')
                    {
                        return new(pos, '>');
                    }

                    Advance();
                    return new(pos, TkGe);
                case '~':
                    Advance();
                    if (Current != '=')
                    {
                        return new(pos, '~');
                    }

                    Advance();
                    return new(pos, TkNe);
                case ':':
                    Advance();
                    if (Current != ':')
                    {
                        return new(pos, ':');
                    }

                    Advance();
                    return new(pos, TkDoubleColon);
                case '"':
                case '\'':
                    return ReadString();
                case EndOfStream:
                    return new(pos, TkEos);
                case '.':
                    SaveAndAdvance();
                    if (CheckNext("."))
                    {
                        if (CheckNext("."))
                        {
                            Buffer.Clear();
                            return new(pos, TkDots);
                        }

                        Buffer.Clear();
                        return new(pos, TkConcat);
                    }

                    if (!IsDigit(Current))
                    {
                        Buffer.Clear();
                        return new(pos, '.');
                    }

                    return ReadNumber(pos);
                case 0:
                    Advance();
                    pos = R.Position;
                    break;
                default:
                    {
                        if (IsDigit(c))
                        {
                            return ReadNumber(pos);
                        }

                        if (IsLetter(c))
                        {
                            for (; IsLetter(c) || IsDigit(c); c = Current)
                            {
                                SaveAndAdvance();
                            }

                            return ReservedOrName();
                        }

                        Advance();
                        return new(pos, c);
                    }
            }
        }
    }

    public void Next()
    {
        LastLine = LineNumber;
        if (LookAheadToken.T != TkEos)
        {
            Token = LookAheadToken;
            LookAheadToken = new(0, TkEos);
        }
        else
        {
            Token = Scan();
        }
    }

    public int LookAhead()
    {
        Assert(LookAheadToken.T == TkEos);
        LookAheadToken = Scan();
        return LookAheadToken.T;
    }

    public bool TestNext(int t)
    {
        var r = Token.T == t;
        if (!r) return false;

        Next();

        return true;
    }

    public void Check(int t)
    {
        if (Token.T != t)
        {
            ErrorExpected(R.Position, (char)t);
        }
    }

    public void CheckMatch(int what, int who, int where)
    {
        if (TestNext(what)) return;

        if (where == LineNumber)
        {
            ErrorExpected(R.Position, (char)what);
        }
        else
        {
            SyntaxError(R.Position, $"{TokenToString(what)} expected (to close {TokenToString(who)} at line {where})");
        }
    }

    static bool IsWhiteSpace(int c) => c is ' ' or '\t' or '\n' or '\r' or '\f' or '\v';
    static bool IsDigit(int c) => c is >= '0' and <= '9';

    static bool IsLetter(int c)
    {
        return c is < ushort.MaxValue and ('_' or >= 'a' and <= 'z' or >= 'A' and <= 'Z');
    }
}