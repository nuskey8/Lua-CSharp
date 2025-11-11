namespace Lua.Runtime;

public partial struct Instruction(uint value)
{
    public const int IABC = 0;
    public const int IABx = 1;
    public const int IAsBx = 2;
    public const int IAx = 3;

    public uint Value = value;

    public static implicit operator Instruction(uint value)
    {
        return new(value);
    }

    public static ReadOnlySpan<string> OpNames => opNames;

    static readonly string[] opNames =
    [
        "MOVE",
        "LOADK",
        "LOADKX",
        "LOADBOOL",
        "LOADNIL",
        "GETUPVAL",
        "GETTABUP",
        "GETTABLE",
        "SETTABUP",
        "SETUPVAL",
        "SETTABLE",
        "NEWTABLE",
        "SELF",
        "ADD",
        "SUB",
        "MUL",
        "DIV",
        "MOD",
        "POW",
        "UNM",
        "NOT",
        "LEN",
        "CONCAT",
        "JMP",
        "EQ",
        "LT",
        "LE",
        "TEST",
        "TESTSET",
        "CALL",
        "TAILCALL",
        "RETURN",
        "FORLOOP",
        "FORPREP",
        "TFORCALL",
        "TFORLOOP",
        "SETLIST",
        "CLOSURE",
        "VARARG",
        "EXTRAARG"
    ];

    /*
    const (
        sizeC             = 9
        sizeB             = 9
        sizeBx            = sizeC + sizeB
        sizeA             = 8
        sizeAx            = sizeC + sizeB + sizeA
        sizeOp            = 6
        posOp             = 0
        posA              = posOp + sizeOp
        posC              = posA + sizeA
        posB              = posC + sizeC
        posBx             = posC
        posAx             = posA
        bitRK             = 1 << (sizeB - 1)
        maxIndexRK        = bitRK - 1
        maxArgAx          = 1<<sizeAx - 1
        maxArgBx          = 1<<sizeBx - 1
        maxArgSBx         = maxArgBx >> 1 // sBx is signed
        maxArgA           = 1<<sizeA - 1
        maxArgB           = 1<<sizeB - 1
        maxArgC           = 1<<sizeC - 1
        listItemsPerFlush = 50 // # list items to accumulate before a setList instruction
    )
    */

    public const int SizeC = 9;
    public const int SizeB = 9;
    public const int SizeBx = SizeC + SizeB;
    public const int SizeA = 8;
    public const int SizeAx = SizeC + SizeB + SizeA;
    public const int SizeOp = 6;
    public const int PosOp = 0;
    public const int PosA = PosOp + SizeOp;
    public const int PosC = PosA + SizeA;
    public const int PosB = PosC + SizeC;
    public const int PosBx = PosC;
    public const int PosAx = PosA;
    public const int BitRK = 1 << (SizeB - 1);
    public const int MaxIndexRK = BitRK - 1;
    public const int MaxArgAx = (1 << SizeAx) - 1;
    public const int MaxArgBx = (1 << SizeBx) - 1;
    public const int MaxArgSBx = MaxArgBx >> 1; // sBx is signed
    public const int MaxArgA = (1 << SizeA) - 1;
    public const int MaxArgB = (1 << SizeB) - 1;
    public const int MaxArgC = (1 << SizeC) - 1;
    public const int ListItemsPerFlush = 50; // # list items to accumulate before a setList instruction

    /*
    func isConstant(x int) bool   { return 0 != x&bitRK }
    func constantIndex(r int) int { return r & ^bitRK }
    func asConstant(r int) int    { return r | bitRK }

    // creates a mask with 'n' 1 bits at position 'p'
    func mask1(n, p uint) instruction { return ^(^instruction(0) << n) << p }
    // creates a mask with 'n' 0 bits at position 'p'
    func mask0(n, p uint) instruction { return ^mask1(n, p) }
    func (i instruction) opCode() opCode         { return opCode(i >> posOp & (1<<sizeOp - 1)) }
    func (i instruction) arg(pos, size uint) int { return int(i >> pos & mask1(size, 0)) }
    func (i *instruction) setOpCode(op opCode)   { i.setArg(posOp, sizeOp, int(op)) }
    func (i *instruction) setArg(pos, size uint, arg int) {
        *i = *i&mask0(size, pos) | instruction(arg)<<pos&mask1(size, pos)}
    */

    public static bool IsConstant(int x)
    {
        return 0 != (x & BitRK);
    }

    public static int ConstantIndex(int r)
    {
        return r & ~BitRK;
    }

    public static int AsConstant(int r)
    {
        return r | BitRK;
    }

    // creates a mask with 'n' 1 bits at position 'p'
    public static uint Mask1(uint n, uint p)
    {
        return (uint)(~(~0 << (int)n) << (int)p);
    }

    // creates a mask with 'n' 0 bits at position 'p'
    public static uint Mask0(uint n, uint p)
    {
        return ~Mask1(n, p);
    }

    public OpCode OpCode
    {
        get => (OpCode)((Value >> PosOp) & ((1 << SizeOp) - 1));
        set => SetArg(PosOp, SizeOp, (byte)value);
    }

    public int Arg(uint pos)
    {
        return (int)((Value >> (int)pos) & Mask1(1, 0));
    }

    public void SetArg(uint pos, uint size, int arg)
    {
        Value = (uint)((Value & Mask0(size, pos)) | ((arg << (int)pos) & Mask1(size, pos)));
    }

    /*
    func (i instruction) a() int   { return int(i >> posA & maxArgA) }
    func (i instruction) b() int   { return int(i >> posB & maxArgB) }
    func (i instruction) c() int   { return int(i >> posC & maxArgC) }
    func (i instruction) bx() int  { return int(i >> posBx & maxArgBx) }
    func (i instruction) ax() int  { return int(i >> posAx & maxArgAx) }
    func (i instruction) sbx() int { return int(i>>posBx&maxArgBx) - maxArgSBx }
    */

    public int A
    {
        get => (int)((Value >> PosA) & MaxArgA);
        set => SetArg(PosA, SizeA, value);
    }

    public int B
    {
        get => (int)((Value >> PosB) & MaxArgB);
        set => SetArg(PosB, SizeB, value);
    }

    public int C
    {
        get => ((int)Value >> PosC) & MaxArgC;
        set => SetArg(PosC, SizeC, value);
    }

    public int Bx
    {
        get => ((int)Value >> PosBx) & MaxArgBx;
        set => SetArg(PosBx, SizeBx, value);
    }

    public int Ax
    {
        get => ((int)Value >> PosAx) & MaxArgAx;
        set => SetArg(PosAx, SizeAx, value);
    }

    public int SBx
    {
        get => (int)((Value >> PosBx) & MaxArgBx) - MaxArgSBx;
        set => SetArg(PosBx, SizeBx, value + MaxArgSBx);
    }

    /*
func createABC(op opCode, a, b, c int) instruction {
    return instruction(op)<<posOp |
        instruction(a)<<posA |
        instruction(b)<<posB |
        instruction(c)<<posC
}

func createABx(op opCode, a, bx int) instruction {
    return instruction(op)<<posOp |
        instruction(a)<<posA |
        instruction(bx)<<posBx
}

func createAx(op opCode, a int) instruction { return instruction(op)<<posOp | instruction(a)<<posAx }
    */

    public static uint CreateABC(OpCode op, int a, int b, int c)
    {
        return (uint)(((byte)op << PosOp) | (a << PosA) | (b << PosB) | (c << PosC));
    }

    public static uint CreateABx(OpCode op, int a, int bx)
    {
        return (uint)(((byte)op << PosOp) | (a << PosA) | (bx << PosBx));
    }

    public static uint CreateAx(OpCode op, int a)
    {
        return (uint)(((byte)op << PosOp) | (a << PosAx));
    }


    /*
    func (i instruction) String() string {
        op := i.opCode()
        s := opNames[op]
        switch opMode(op) {
        case iABC:
            s = fmt.Sprintf("%s %d", s, i.a())
            if bMode(op) == opArgK && isConstant(i.b()) {
                s = fmt.Sprintf("%s constant %d", s, constantIndex(i.b()))
            } else if bMode(op) != opArgN {
                s = fmt.Sprintf("%s %d", s, i.b())
            }
            if cMode(op) == opArgK && isConstant(i.c()) {
                s = fmt.Sprintf("%s constant %d", s, constantIndex(i.c()))
            } else if cMode(op) != opArgN {
                s = fmt.Sprintf("%s %d", s, i.c())
            }
        case iAsBx:
            s = fmt.Sprintf("%s %d", s, i.a())
            if bMode(op) != opArgN {
                s = fmt.Sprintf("%s %d", s, i.sbx())
            }
        case iABx:
            s = fmt.Sprintf("%s %d", s, i.a())
            if bMode(op) != opArgN {
                s = fmt.Sprintf("%s %d", s, i.bx())
            }
        case iAx:
            s = fmt.Sprintf("%s %d", s, i.ax())
        }
        return s
    }
    */

    public override string ToString()
    {
        var op = OpCode;
        var s = OpNames[(byte)op];
        switch (OpMode(op))
        {
            case IABC:
                s = $"{s} {A}";
                if (BMode(op) == OpArgK && IsConstant(B))
                {
                    s = $"{s} -{1 + ConstantIndex(B)}";
                }
                else if (BMode(op) != OpArgN)
                {
                    s = $"{s} {B}";
                }

                if (CMode(op) == OpArgK && IsConstant(C))
                {
                    s = $"{s} -{1 + ConstantIndex(C)}";
                }
                else if (CMode(op) != OpArgN)
                {
                    s = $"{s} {C}";
                }

                // s = $"{s} {A}";
                // if (BMode(op) == OpArgK && IsConstant(B))
                // 	s = $"{s} constant {ConstantIndex(B)}";
                // else if (BMode(op) != OpArgN)
                // 	s = $"{s} {B}";
                // if (CMode(op) == OpArgK && IsConstant(C))
                // 	s = $"{s} constant {ConstantIndex(C)}";
                // else if (CMode(op) != OpArgN)
                // 	s = $"{s} {C}";
                break;
            case IAsBx:
                s = $"{s} {A}";
                if (BMode(op) != OpArgN)
                {
                    s = $"{s} {SBx}";
                }

                break;
            case IABx:
                s = $"{s} {A}";
                if (BMode(op) != OpArgN)
                {
                    s = $"{s} {Bx}";
                }

                break;
            case IAx:
                s = $"{s} {Ax}";
                break;
        }

        return s;
    }

    /*
    func opmode(t, a, b, c, m int) byte { return byte(t<<7 | a<<6 | b<<4 | c<<2 | m) }
    */

    public static byte OpMode(int t, int a, int b, int c, int m)
    {
        return (byte)((t << 7) | (a << 6) | (b << 4) | (c << 2) | m);
    }

    /*
    const (
        opArgN = iota // argument is not used
        opArgU        // argument is used
        opArgR        // argument is a register or a jump offset
        opArgK        // argument is a constant or register/constant
    )
    */

    public const int OpArgN = 0;
    public const int OpArgU = 1;
    public const int OpArgR = 2;

    public const int OpArgK = 3;

    /*
    func opMode(m opCode) int     { return int(opModes[m] & 3) }
    func bMode(m opCode) byte     { return (opModes[m] >> 4) & 3 }
    func cMode(m opCode) byte     { return (opModes[m] >> 2) & 3 }
    func testAMode(m opCode) bool { return opModes[m]&(1<<6) != 0 }
    func testTMode(m opCode) bool { return opModes[m]&(1<<7) != 0 }
    */

    public static int OpMode(OpCode m)
    {
        return (int)(opModes[(byte)m] & 3);
    }

    public static byte BMode(OpCode m)
    {
        return (byte)((opModes[(byte)m] >> 4) & 3);
    }

    public static byte CMode(OpCode m)
    {
        return (byte)((opModes[(byte)m] >> 2) & 3);
    }

    public static bool TestAMode(OpCode m)
    {
        return (opModes[(byte)m] & (1 << 6)) != 0;
    }

    public static bool TestTMode(OpCode m)
    {
        return (opModes[(byte)m] & (1 << 7)) != 0;
    }

    /*
    var opModes []byte = []byte{
    //     T  A    B       C     mode		    opcode
    opmode(0, 1, opArgR, opArgN, iABC),  // opMove
    opmode(0, 1, opArgK, opArgN, iABx),  // opLoadConstant
    opmode(0, 1, opArgN, opArgN, iABx),  // opLoadConstantEx
    opmode(0, 1, opArgU, opArgU, iABC),  // opLoadBool
    opmode(0, 1, opArgU, opArgN, iABC),  // opLoadNil
    opmode(0, 1, opArgU, opArgN, iABC),  // opGetUpValue
    opmode(0, 1, opArgU, opArgK, iABC),  // opGetTableUp
    opmode(0, 1, opArgR, opArgK, iABC),  // opGetTable
    opmode(0, 0, opArgK, opArgK, iABC),  // opSetTableUp
    opmode(0, 0, opArgU, opArgN, iABC),  // opSetUpValue
    opmode(0, 0, opArgK, opArgK, iABC),  // opSetTable
    opmode(0, 1, opArgU, opArgU, iABC),  // opNewTable
    opmode(0, 1, opArgR, opArgK, iABC),  // opSelf
    opmode(0, 1, opArgK, opArgK, iABC),  // opAdd
    opmode(0, 1, opArgK, opArgK, iABC),  // opSub
    opmode(0, 1, opArgK, opArgK, iABC),  // opMul
    opmode(0, 1, opArgK, opArgK, iABC),  // opDiv
    opmode(0, 1, opArgK, opArgK, iABC),  // opMod
    opmode(0, 1, opArgK, opArgK, iABC),  // opPow
    opmode(0, 1, opArgR, opArgN, iABC),  // opUnaryMinus
    opmode(0, 1, opArgR, opArgN, iABC),  // opNot
    opmode(0, 1, opArgR, opArgN, iABC),  // opLength
    opmode(0, 1, opArgR, opArgR, iABC),  // opConcat
    opmode(0, 0, opArgR, opArgN, iAsBx), // opJump
    opmode(1, 0, opArgK, opArgK, iABC),  // opEqual
    opmode(1, 0, opArgK, opArgK, iABC),  // opLessThan
    opmode(1, 0, opArgK, opArgK, iABC),  // opLessOrEqual
    opmode(1, 0, opArgN, opArgU, iABC),  // opTest
    opmode(1, 1, opArgR, opArgU, iABC),  // opTestSet
    opmode(0, 1, opArgU, opArgU, iABC),  // opCall
    opmode(0, 1, opArgU, opArgU, iABC),  // opTailCall
    opmode(0, 0, opArgU, opArgN, iABC),  // opReturn
    opmode(0, 1, opArgR, opArgN, iAsBx), // opForLoop
    opmode(0, 1, opArgR, opArgN, iAsBx), // opForPrep
    opmode(0, 0, opArgN, opArgU, iABC),  // opTForCall
    opmode(0, 1, opArgR, opArgN, iAsBx), // opTForLoop
    opmode(0, 0, opArgU, opArgU, iABC),  // opSetList
    opmode(0, 1, opArgU, opArgN, iABx),  // opClosure
    opmode(0, 1, opArgU, opArgN, iABC),  // opVarArg
    opmode(0, 0, opArgU, opArgU, iAx),   // opExtraArg
    }
    */

    public static ReadOnlySpan<byte> OpModes => opModes;

    static readonly byte[] opModes =
    [
        //         T   A    B         C          mode	opcode]
        OpMode(0, 1, OpArgR, OpArgN, IABC),  // opMove
        OpMode(0, 1, OpArgK, OpArgN, IABx),  // opLoadConstant
        OpMode(0, 1, OpArgN, OpArgN, IABx),  // opLoadConstantEx
        OpMode(0, 1, OpArgU, OpArgU, IABC),  // opLoadBool
        OpMode(0, 1, OpArgU, OpArgN, IABC),  // opLoadNil
        OpMode(0, 1, OpArgU, OpArgN, IABC),  // opGetUpValue
        OpMode(0, 1, OpArgU, OpArgK, IABC),  // opGetTableUp
        OpMode(0, 1, OpArgR, OpArgK, IABC),  // opGetTable
        OpMode(0, 0, OpArgK, OpArgK, IABC),  // opSetTableUp
        OpMode(0, 0, OpArgU, OpArgN, IABC),  // opSetUpValue
        OpMode(0, 0, OpArgK, OpArgK, IABC),  // opSetTable
        OpMode(0, 1, OpArgU, OpArgU, IABC),  // opNewTable
        OpMode(0, 1, OpArgR, OpArgK, IABC),  // opSelf
        OpMode(0, 1, OpArgK, OpArgK, IABC),  // opAdd
        OpMode(0, 1, OpArgK, OpArgK, IABC),  // opSub
        OpMode(0, 1, OpArgK, OpArgK, IABC),  // opMul
        OpMode(0, 1, OpArgK, OpArgK, IABC),  // opDiv
        OpMode(0, 1, OpArgK, OpArgK, IABC),  // opMod
        OpMode(0, 1, OpArgK, OpArgK, IABC),  // opPow
        OpMode(0, 1, OpArgR, OpArgN, IABC),  // opUnaryMinus
        OpMode(0, 1, OpArgR, OpArgN, IABC),  // opNot
        OpMode(0, 1, OpArgR, OpArgN, IABC),  // opLength
        OpMode(0, 1, OpArgR, OpArgR, IABC),  // opConcat
        OpMode(0, 0, OpArgR, OpArgN, IAsBx), // opJump
        OpMode(1, 0, OpArgK, OpArgK, IABC),  // opEqual
        OpMode(1, 0, OpArgK, OpArgK, IABC),  // opLessThan
        OpMode(1, 0, OpArgK, OpArgK, IABC),  // opLessOrEqual
        OpMode(1, 0, OpArgN, OpArgU, IABC),  // opTest
        OpMode(1, 1, OpArgR, OpArgU, IABC),  // opTestSet
        OpMode(0, 1, OpArgU, OpArgU, IABC),  // opCall
        OpMode(0, 1, OpArgU, OpArgU, IABC),  // opTailCall
        OpMode(0, 0, OpArgU, OpArgN, IABC),  // opReturn
        OpMode(0, 1, OpArgR, OpArgN, IAsBx), // opForLoop
        OpMode(0, 1, OpArgR, OpArgN, IAsBx), // opForPrep
        OpMode(0, 0, OpArgN, OpArgU, IABC),  // opTForCall
        OpMode(0, 1, OpArgR, OpArgN, IAsBx), // opTForLoop
        OpMode(0, 0, OpArgU, OpArgU, IABC),  // opSetList
        OpMode(0, 1, OpArgU, OpArgN, IABx),  // opClosure
        OpMode(0, 1, OpArgU, OpArgN, IABC),  // opVarArg
        OpMode(0, 0, OpArgU, OpArgU, IAx)    // opExtraArg
    ];

    /// <summary>
    /// R(A) := R(B)
    /// </summary>
    public static Instruction Move(byte a, ushort b)
    {
        return new() { OpCode = OpCode.Move, A = a, B = b };
    }

    /// <summary>
    /// R(A) := Kst(Bx)
    /// </summary>
    public static Instruction LoadK(byte a, uint bx)
    {
        return new() { OpCode = OpCode.LoadK, A = a, Bx = (int)bx };
    }

    /// <summary>
    /// R(A) := Kst(extra arg)
    /// </summary>
    public static Instruction LoadKX(byte a)
    {
        return new() { OpCode = OpCode.LoadKX, A = a };
    }

    /// <summary>
    /// <para>R(A) := (Bool)B</para>
    /// <para>if (C) pc++</para>
    /// </summary>
    public static Instruction LoadBool(byte a, ushort b, ushort c)
    {
        return new() { OpCode = OpCode.LoadBool, A = a, B = b, C = c };
    }

    /// <summary>
    /// R(A), R(A+1), ..., R(A+B) := nil
    /// </summary>
    public static Instruction LoadNil(byte a, ushort b)
    {
        return new() { OpCode = OpCode.LoadNil, A = a, B = b };
    }

    /// <summary>
    /// R(A) := UpValue[B]
    /// </summary>
    public static Instruction GetUpVal(byte a, ushort b)
    {
        return new() { OpCode = OpCode.GetUpVal, A = a, B = b };
    }

    /// <summary>
    /// R(A) := UpValue[B][RK(C)]
    /// </summary>
    public static Instruction GetTabUp(byte a, ushort b, ushort c)
    {
        return new() { OpCode = OpCode.GetTabUp, A = a, B = b, C = c };
    }

    /// <summary>
    /// R(A) := R(B)[RK(C)]
    /// </summary>
    public static Instruction GetTable(byte a, ushort b, ushort c)
    {
        return new() { OpCode = OpCode.GetTable, A = a, B = b, C = c };
    }

    /// <summary>
    /// UpValue[B] := R(A)
    /// </summary>
    public static Instruction SetUpVal(byte a, ushort b)
    {
        return new() { OpCode = OpCode.SetUpVal, A = a, B = b };
    }

    /// <summary>
    /// UpValue[A][RK(B)] := RK(C)
    /// </summary>
    public static Instruction SetTabUp(byte a, ushort b, ushort c)
    {
        return new() { OpCode = OpCode.SetTabUp, A = a, B = b, C = c };
    }

    /// <summary>
    /// R(A)[RK(B)] := RK(C)
    /// </summary>
    public static Instruction SetTable(byte a, ushort b, ushort c)
    {
        return new() { OpCode = OpCode.SetTable, A = a, B = b, C = c };
    }

    /// <summary>
    /// R(A) := {} (size = B,C)
    /// </summary>
    public static Instruction NewTable(byte a, ushort b, ushort c)
    {
        return new() { OpCode = OpCode.NewTable, A = a, B = b, C = c };
    }

    /// <summary>
    /// R(A+1) := R(B); R(A) := R(B)[RK(C)]
    /// </summary>
    public static Instruction Self(byte a, ushort b, ushort c)
    {
        return new() { OpCode = OpCode.Self, A = a, B = b, C = c };
    }

    /// <summary>
    /// R(A) := RK(B) + RK(C)
    /// </summary>
    public static Instruction Add(byte a, ushort b, ushort c)
    {
        return new() { OpCode = OpCode.Add, A = a, B = b, C = c };
    }

    /// <summary>
    /// R(A) := RK(B) - RK(C)
    /// </summary>
    public static Instruction Sub(byte a, ushort b, ushort c)
    {
        return new() { OpCode = OpCode.Sub, A = a, B = b, C = c };
    }

    /// <summary>
    /// R(A) := RK(B) * RK(C)
    /// </summary>
    public static Instruction Mul(byte a, ushort b, ushort c)
    {
        return new() { OpCode = OpCode.Mul, A = a, B = b, C = c };
    }

    /// <summary>
    /// R(A) := RK(B) / RK(C)
    /// </summary>
    public static Instruction Div(byte a, ushort b, ushort c)
    {
        return new() { OpCode = OpCode.Div, A = a, B = b, C = c };
    }

    /// <summary>
    /// R(A) := RK(B) % RK(C)
    /// </summary>
    public static Instruction Mod(byte a, ushort b, ushort c)
    {
        return new() { OpCode = OpCode.Mod, A = a, B = b, C = c };
    }

    /// <summary>
    /// R(A) := RK(B) ^ RK(C)
    /// </summary>
    public static Instruction Pow(byte a, ushort b, ushort c)
    {
        return new() { OpCode = OpCode.Pow, A = a, B = b, C = c };
    }

    /// <summary>
    /// R(A) := -R(B)
    /// </summary>
    public static Instruction Unm(byte a, ushort b)
    {
        return new() { OpCode = OpCode.Unm, A = a, B = b };
    }

    /// <summary>
    /// R(A) := not R(B)
    /// </summary>
    public static Instruction Not(byte a, ushort b)
    {
        return new() { OpCode = OpCode.Not, A = a, B = b };
    }

    /// <summary>
    /// R(A) := length of R(B)
    /// </summary>
    public static Instruction Len(byte a, ushort b)
    {
        return new() { OpCode = OpCode.Len, A = a, B = b };
    }

    /// <summary>
    /// R(A) := R(B).. ... ..R(C)
    /// </summary>
    public static Instruction Concat(byte a, ushort b, ushort c)
    {
        return new() { OpCode = OpCode.Concat, A = a, B = b, C = c };
    }

    /// <summary>
    /// <para>pc += sBx</para>
    /// <para>if (A) close all upvalues >= R(A - 1)</para>
    /// </summary>
    public static Instruction Jmp(byte a, int sBx)
    {
        return new() { OpCode = OpCode.Jmp, A = a, SBx = sBx };
    }

    /// <summary>
    /// if ((RK(B) == RK(C)) ~= A) then pc++
    /// </summary>
    public static Instruction Eq(byte a, ushort b, ushort c)
    {
        return new() { OpCode = OpCode.Eq, A = a, B = b, C = c };
    }

    /// <summary>
    /// if ((RK(B) &lt; RK(C)) ~= A) then pc++
    /// </summary>
    public static Instruction Lt(byte a, ushort b, ushort c)
    {
        return new() { OpCode = OpCode.Lt, A = a, B = b, C = c };
    }

    /// <summary>
    /// if ((RK(B) &lt;= RK(C)) ~= A) then pc++
    /// </summary>
    public static Instruction Le(byte a, ushort b, ushort c)
    {
        return new() { OpCode = OpCode.Le, A = a, B = b, C = c };
    }

    /// <summary>
    /// if not (R(A) &lt;=&gt; C) then pc++
    /// </summary>
    public static Instruction Test(byte a, ushort c)
    {
        return new() { OpCode = OpCode.Test, A = a, C = c };
    }

    /// <summary>
    /// if (R(B) &lt;=&gt; C) then R(A) := R(B) else pc++
    /// </summary>
    public static Instruction TestSet(byte a, ushort b, ushort c)
    {
        return new() { OpCode = OpCode.TestSet, A = a, B = b, C = c };
    }

    /// <summary>
    /// R(A), ... ,R(A+C-2) := R(A)(R(A+1), ... ,R(A+B-1))
    /// </summary>
    public static Instruction Call(byte a, ushort b, ushort c)
    {
        return new() { OpCode = OpCode.Call, A = a, B = b, C = c };
    }

    /// <summary>
    /// return R(A)(R(A+1), ... ,R(A+B-1))
    /// </summary>
    public static Instruction TailCall(byte a, ushort b, ushort c)
    {
        return new() { OpCode = OpCode.TailCall, A = a, B = b, C = c };
    }

    /// <summary>
    /// return R(A), ... ,R(A+B-2)
    /// </summary>
    public static Instruction Return(byte a, ushort b)
    {
        return new() { OpCode = OpCode.Return, A = a, B = b };
    }

    /// <summary>
    /// <para>R(A) += R(A+2);</para>
    /// <para>if R(A) &lt;?= R(A+1) then { pc += sBx; R(A+3) = R(A) }</para>
    /// </summary>
    public static Instruction ForLoop(byte a, int sBx)
    {
        return new() { OpCode = OpCode.ForLoop, A = a, SBx = sBx };
    }

    /// <summary>
    /// <para>R(A) -= R(A+2)</para>
    /// <para>pc += sBx</para>
    /// </summary>
    public static Instruction ForPrep(byte a, int sBx)
    {
        return new() { OpCode = OpCode.ForPrep, A = a, SBx = sBx };
    }

    /// <summary>
    /// R(A+3), ... ,R(A+2+C) := R(A)(R(A+1), R(A+2));
    /// </summary>
    public static Instruction TForCall(byte a, ushort c)
    {
        return new() { OpCode = OpCode.TForCall, A = a, C = c };
    }

    /// <summary>
    /// if R(A+1) ~= nil then { R(A) = R(A+1); pc += sBx }
    /// </summary>
    public static Instruction TForLoop(byte a, int sBx)
    {
        return new() { OpCode = OpCode.TForLoop, A = a, SBx = sBx };
    }

    /// <summary>
    /// R(A)[(C-1) * FPF + i] := R(A+i), 1 &lt;= i &lt;= B
    /// </summary>
    public static Instruction SetList(byte a, ushort b, ushort c)
    {
        return new() { OpCode = OpCode.SetList, A = a, B = b, C = c };
    }

    /// <summary>
    /// R(A) := closure(KPROTO[Bx])
    /// </summary>
    public static Instruction Closure(byte a, int sBx)
    {
        return new() { OpCode = OpCode.Closure, A = a, SBx = sBx };
    }

    /// <summary>
    /// R(A), R(A+1), ..., R(A+B-2) = vararg
    /// </summary>
    public static Instruction VarArg(byte a, ushort b)
    {
        return new() { OpCode = OpCode.VarArg, A = a, B = b };
    }

    /// <summary>
    /// extra (larger) argument for previous opcode
    /// </summary>
    public static Instruction ExtraArg(uint ax)
    {
        return new() { OpCode = OpCode.ExtraArg, Ax = (int)ax };
    }
}