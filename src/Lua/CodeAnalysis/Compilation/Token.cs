using System.Diagnostics;

namespace Lua.CodeAnalysis.Compilation;

[DebuggerDisplay("{DebuggerDisplay}")]
internal struct Token
{
    public int T;
    public double N;
    public string S;
    string DebuggerDisplay => $"{Scanner.TokenToString(this)} {T} {N} {S}";

    public static implicit operator Token(int token)
    {
        return new Token { T = token };
    }
}