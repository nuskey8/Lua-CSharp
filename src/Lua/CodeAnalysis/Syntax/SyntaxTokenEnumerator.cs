using System.Runtime.CompilerServices;

namespace Lua.CodeAnalysis.Syntax;

public ref struct SyntaxTokenEnumerator(ReadOnlySpan<SyntaxToken> source)
{
    ReadOnlySpan<SyntaxToken> source = source;
    SyntaxToken current;
    int offset;

    public SyntaxToken Current => current;
    public int Position => offset;
    public bool IsCompleted => source.Length == offset;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        if (IsCompleted) return false;
        current = source[offset];
        offset++;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MovePrevious()
    {
        if (offset == 0) return false;
        offset--;
        current = source[offset - 1];
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SkipEoL()
    {
        while (true)
        {
            if (current.Type != SyntaxTokenType.EndOfLine) return;
            if (!MoveNext()) return;
        }
    }

    public SyntaxToken GetNext(bool skipEoL = false)
    {
        if (!skipEoL)
        {
            return IsCompleted ? default : source[offset];
        }

        var i = offset;
        while (i < source.Length)
        {
            var c = source[i];
            if (source[i].Type is not SyntaxTokenType.EndOfLine) return c;
            i++;
        }

        return default;
    }
}