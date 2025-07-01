using System.Diagnostics;

namespace Lua.CodeAnalysis.Compilation;

[DebuggerDisplay("{DebuggerDisplay}")]
internal readonly struct Token(int pos, int t)
{
    public Token(int pos, int t, string str) : this(pos, t)
    {
        S = str;
        N = 0;
    }

    public Token(int pos, double n) : this(pos, Scanner.TkNumber)
    {
        N = n;
        S = string.Empty;
    }

    public readonly int Pos = pos;
    public readonly int T = t;
    public readonly double N;
    public readonly string S = "";
    string DebuggerDisplay => $"{Scanner.TokenToString(this)} {T} {N} {S}";
}