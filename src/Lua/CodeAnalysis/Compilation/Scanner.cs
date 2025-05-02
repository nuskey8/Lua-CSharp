using Lua.Internal;
using System.Globalization;
using System.Text;
using static System.Diagnostics.Debug;

namespace Lua.CodeAnalysis.Compilation;

using static Constants;

internal struct Scanner
{
    public LuaState L;
    public StringBuilder Buffer;
    public TextReader R;
    public int Current;
    public int LineNumber, LastLine;
    public string Source;
    public Token LookAheadToken;

    ///inline
    public Token Token;

    public int T => Token.T;


    static string ChunkID(string source)
    {
        var shortSourceBuffer = (stackalloc char[59]);
        var len = LuaDebug.WriteShortSource(source, shortSourceBuffer);
        return shortSourceBuffer[..len].ToString();
    }


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
    public const int TkGE = TkEq + 1;
    public const int TkLE = TkGE + 1;
    public const int TkNE = TkLE + 1;
    public const int TkDoubleColon = TkNE + 1;
    public const int TkEOS = TkDoubleColon + 1;
    public const int TkNumber = TkEOS + 1;
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


    public void SyntaxError(string message) => ScanError(message, Token.T);
    public void ErrorExpected(char t) => SyntaxError(TokenToString(t) + " expected");
    public void NumberError() => ScanError("malformed number", TkNumber);
    public static bool IsNewLine(int c) => c == '\n' || c == '\r';

    public static bool IsDecimal(int c) => '0' <= c && c <= '9';


    public static string TokenToString(Token t) => t.T switch
    {
        TkName or TkString => t.S,
        TkNumber => $"{t.N}",
        < FirstReserved => $"{(char)t.T}", // TODO check for printable rune
        < TkEOS => $"'{tokens[t.T - FirstReserved]}'",
        _ => tokens[t.T - FirstReserved]
    };

    public string TokenToString(int t) => t switch
    {
        TkName or TkString => Token.S,
        TkNumber => $"{Token.N}",
        < FirstReserved => $"{(char)t}", // TODO check for printable rune
        < TkEOS => $"'{tokens[t - FirstReserved]}'",
        _ => tokens[t - FirstReserved]
    };

    public static string TokenRuteToString(int t) => t switch
    {
        < FirstReserved => $"{(char)t}", // TODO check for printable rune
        <= TkString => $"'{tokens[t - FirstReserved]}'",
        _ => tokens[t - FirstReserved]
    };


    public void ScanError(string message, int token)
    {
        var buff = ChunkID(Source);
        if (token != 0) message = $"{buff}:{LineNumber}: {message} near {TokenToString(token)}";
        else message = $"{buff}:{LineNumber}: {message}";
        throw new LuaScanException(message);
    }


    public void IncrementLineNumber()
    {
        var old = Current;
        Assert(IsNewLine(old));
        Advance();
        if (IsNewLine(Current) && Current != old) Advance();
        if (++LineNumber >= MaxLine) SyntaxError("chunk has too many lines");
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
        Buffer.Append((char)c);
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
        Assert(c == '[' || c == ']');
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
                    if (comment)
                    {
                        ScanError("unfinished long comment", TkEOS);
                    }
                    else
                    {
                        ScanError("unfinished long string", TkEOS);
                    }

                    break;
                case ']':
                    if (SkipSeparator() == sep)
                    {
                        SaveAndAdvance();
                        if (!comment)
                        {
                            var s = Buffer.ToString(2 + sep, Buffer.Length - (4 + 2 * sep));
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


    public (double n, int c, int i) ReadHexNumber(double x)
    {
        var c = Current;
        var n = x;
        if (!IsHexadecimal(c))
        {
            return (n, c, 0);
        }

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
                default:
                    return (n, c, i);
            }

            Advance();
            (c, n, i) = (Current, n * 16.0 + c, i + 1);
        }
    }


    public Token ReadNumber()
    {
        var c = Current;
        Assert(IsDecimal(c));
        SaveAndAdvance();
        if (c == '0' && CheckNext("Xx")) // hexadecimal
        {
            Buffer.Clear();
            var exponent = 0;
            (var fraction, c, var i) = ReadHexNumber(0);
            if (c == '.')
            {
                Advance();
                (fraction, c, exponent) = ReadHexNumber(fraction);
            }

            if (i == 0 && exponent == 0)
            {
                NumberError();
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
                    NumberError();
                }

                _ = ReadDigits();

                if (!long.TryParse(Buffer.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out long e))
                {
                    NumberError();
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

            return new() { T = TkNumber, N = (fraction * Math.Pow(2, exponent)) };
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

        var str = Buffer.ToString();
        if (str.StartsWith("0"))
        {
            if (str.Length == 1)
            {
                Buffer.Clear();
                return new() { T = TkNumber, N = 0 };
            }

            str = str.TrimStart('0');
            if (!IsDecimal(str[0]))
            {
                str = "0" + str;
            }
        }

        if (!double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out double f))
        {
            NumberError();
        }

        Buffer.Clear();
        return new() { T = TkNumber, N = f };
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


    public void EscapeError(ReadOnlySpan<int> c, string message)
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
        Token.S = Buffer.ToString();
        Buffer.Clear();
        ScanError(message, TkString);
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
                    EscapeError(b.Slice(0, i + 1), "hexadecimal digit expected");
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
        for (int i = 0; i < b.Length && IsDecimal(c); i++, c = Current)
        {
            b[i] = c;
            r = 10 * r + c - '0';
            Advance();
        }

        if (r > 255)
        {
            EscapeError(b, "decimal escape too large");
        }

        return r;
    }


    public Token ReadString()
    {
        var delimiter = Current;
        for (SaveAndAdvance(); Current != delimiter;)
        {
            switch (Current)
            {
                case EndOfStream:
                    ScanError("unfinished string", TkEOS);
                    break;
                case '\n' or '\r':
                    ScanError("unfinished string", TkString);
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
                        EscapeError([c], "invalid escape sequence");
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
        var str = Buffer.ToString(1, length);
        Buffer.Clear();
        return new() { T = TkString, S = str };
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
        var str = Buffer.ToString();
        Buffer.Clear();
        for (var i = 0; i < Tokens.Length; i++)
        {
            if (str == Tokens[i])
            {
                return new() { T = (i + FirstReserved), S = str };
            }
        }

        return new() { T = TkName, S = str };
    }


    public Token Scan()
    {
        const bool comment = true, str = false;
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
                    break;
                case '-':
                    Advance();
                    if (Current != '-')
                    {
                        return new() { T = '-' };
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
                            return new() { T = TkString, S = ReadMultiLine(str, sep) };
                        }

                        Buffer.Clear();
                        if (sep == -1) return new() { T = '[' };

                        ScanError("invalid long string delimiter", TkString);
                        break;
                    }
                case '=':
                    Advance();
                    if (Current != '=')
                    {
                        return new() { T = '=' };
                    }

                    Advance();
                    return new() { T = TkEq };
                case '<':
                    Advance();
                    if (Current != '=')
                    {
                        return new() { T = '<' };
                    }

                    Advance();
                    return new() { T = TkLE };
                case '>':
                    Advance();
                    if (Current != '=')
                    {
                        return new() { T = '>' };
                    }

                    Advance();
                    return new() { T = TkGE };
                case '~':
                    Advance();
                    if (Current != '=')
                    {
                        return new() { T = '~' };
                    }

                    Advance();
                    return new() { T = TkNE };
                case ':':
                    Advance();
                    if (Current != ':')
                    {
                        return new() { T = ':' };
                    }

                    Advance();
                    return new() { T = TkDoubleColon };
                case '"':
                case '\'':
                    return ReadString();
                case EndOfStream:
                    return new() { T = TkEOS };
                case '.':
                    SaveAndAdvance();
                    if (CheckNext("."))
                    {
                        if (CheckNext("."))
                        {
                            Buffer.Clear();
                            return new() { T = TkDots };
                        }

                        Buffer.Clear();
                        return new() { T = TkConcat };
                    }

                    if (!IsDigit(Current))
                    {
                        Buffer.Clear();
                        return new() { T = '.' };
                    }

                    return ReadNumber();
                case 0:
                    Advance();
                    break;
                default:
                    {
                        if (IsDigit(c))
                        {
                            return ReadNumber();
                        }

                        if (c == '_' || IsLetter(c))
                        {
                            for (; c == '_' || IsLetter(c) || IsDigit(c); c = Current)
                            {
                                SaveAndAdvance();
                            }

                            return ReservedOrName();
                        }

                        Advance();
                        return new() { T = c };
                    }
            }
        }
    }


    public void Next()
    {
        LastLine = LineNumber;
        if (LookAheadToken.T != TkEOS)
        {
            Token = LookAheadToken;
            LookAheadToken.T = TkEOS;
        }
        else
        {
            Token = Scan();
        }
    }


    public int LookAhead()
    {
        Assert(LookAheadToken.T == TkEOS);
        LookAheadToken = Scan();
        return LookAheadToken.T;
    }


    public bool TestNext(int t)
    {
        var r = Token.T == t;
        if (r)
        {
            Next();
        }

        return r;
    }


    public void Check(int t)
    {
        if (Token.T != t)
        {
            ErrorExpected((char)t);
        }
    }


    public void CheckMatch(int what, int who, int where)
    {
        if (!TestNext(what))
        {
            if (where == LineNumber)
            {
                ErrorExpected((char)what);
            }
            else
            {
                SyntaxError($"{TokenToString(what)} expected (to close {TokenToString(who)} at line {where})");
            }
        }
    }

    static bool IsWhiteSpace(int c) => c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v';
    static bool IsDigit(int c) => c is >= '0' and <= '9';

    static bool IsLetter(int c)
    {
        return c < ushort.MaxValue && c is '_' or >= 'a' and <= 'z' or >= 'A' and <= 'Z';
    }
}