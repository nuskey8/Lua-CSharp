using Lua.Internal;
using System.Runtime.CompilerServices;

namespace Lua.CodeAnalysis.Compilation;

unsafe struct TextReader(char* ptr, int length)
{
    public int Position;

    public (char, bool) Read()
    {
        if (Position >= length) return ('\0', false);
        return (ptr[Position++], true);
    }

    public bool TryRead(out char c)
    {
        if (Position >= length)
        {
            c = '\0';
            return false;
        }

        c = ptr[Position++];
        return true;
    }

    public char Current => ptr[Position];

    public ReadOnlySpan<char> Span => new(ptr, length);
}

internal unsafe struct AssignmentTarget(ref AssignmentTarget previous, ExprDesc exprDesc)
{
    public readonly AssignmentTarget* Previous = (AssignmentTarget*)Unsafe.AsPointer(ref previous);
    public ExprDesc Description = exprDesc;
}

internal struct Label
{
    public string Name;
    public int Pc, Line;
    public int ActiveVariableCount;
}

internal class Block : IPoolNode<Block>
{
    public Block? Previous;
    public int FirstLabel, FirstGoto;
    public int ActiveVariableCount;
    public bool HasUpValue, IsLoop;
    Block() { }
    ref Block? IPoolNode<Block>.NextNode => ref Previous;

    static LinkedPool<Block> Pool;

    public static Block Get(Block? previous, int firstLabel, int firstGoto, int activeVariableCount, bool hasUpValue, bool isLoop)
    {
        if (!Pool.TryPop(out var block))
        {
            block = new Block();
        }

        block.Previous = previous;
        block.FirstLabel = firstLabel;
        block.FirstGoto = firstGoto;
        block.ActiveVariableCount = activeVariableCount;
        block.HasUpValue = hasUpValue;
        block.IsLoop = isLoop;


        return block;
    }

    public void Release()
    {
        Previous = null;
        Pool.TryPush(this);
    }
}

internal struct ExprDesc
{
    public Kind Kind;
    public int Index;
    public int Table;
    public Kind TableType;
    public int Info;
    public int T, F;
    public double Value;
    public readonly bool HasJumps() => T != F;

    public readonly bool IsNumeral() => Kind == Kind.Number && T == Function.NoJump && F == Function.NoJump;

    public readonly bool IsVariable() => Kind.Local <= Kind && Kind <= Kind.Indexed;

    public readonly bool HasMultipleReturns() => Kind == Kind.Call || Kind == Kind.VarArg;
}

internal enum Kind
{
    Void = 0,
    Nil = 1,
    True = 2,
    False = 3,
    Constant = 4,
    Number = 5,
    NonRelocatable = 6,
    Local = 7,
    UpValue = 8,
    Indexed = 9,
    Jump = 10,
    Relocatable = 11,
    Call = 12,
    VarArg = 13
}