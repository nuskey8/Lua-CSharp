using System.Diagnostics;

namespace Lua.CodeAnalysis.Compilation;

[DebuggerDisplay("{DebuggerDisplay}")]
readonly struct Token(int pos, int t, int rawLength = 0)
{
    public Token(int pos, int t, string str) : this(pos, t)
    {
        S = str;
        N = 0;
    }

    public Token(int pos, int t, string str, int rawLength) : this(pos, t, rawLength)
    {
        S = str;
        N = 0;
    }

    public Token(int pos, double n, int rawLength) : this(pos, Scanner.TkNumber, rawLength)
    {
        N = n;
        S = string.Empty;
    }

    public readonly int Pos = pos;
    public readonly int T = t;
    public readonly int RawLength = rawLength;
    public readonly double N;
    public readonly string S = "";

    string DebuggerDisplay
    {
        get
        {
            return $"{Scanner.TokenToString(this)} {T} {N} {S}";
        }
    }
}
