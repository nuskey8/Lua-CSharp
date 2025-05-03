using Lua.Internal;
using Lua.Runtime;
using System.Diagnostics;
using Constants = Lua.Internal.Constants;

namespace Lua.CodeAnalysis.Compilation;

using static Debug;
using static Instruction;
using static Constants;

internal class Function : IPoolNode<Function>
{
    public readonly Dictionary<LuaValue, int> ConstantLookup = new();
    public PrototypeBuilder Proto = null!;
    public Function? Previous;
    public Parser P = null!;
    public Block Block = null!;
    public int JumpPc = NoJump, LastTarget;
    public int FreeRegisterCount;
    public int ActiveVariableCount;
    public int FirstLocal;

    static LinkedPool<Function> pool;

    ref Function? IPoolNode<Function>.NextNode => ref Previous;

    internal static Function Get(Parser p, PrototypeBuilder proto)
    {
        if (!pool.TryPop(out var f))
        {
            f = new Function();
        }

        f.P = p;
        f.Proto = proto;
        return f;
    }

    internal void Release()
    {
        Previous = null;
        ConstantLookup.Clear();
        JumpPc = NoJump;
        Proto = null!;
        LastTarget = 0;
        FreeRegisterCount = 0;
        ActiveVariableCount = 0;
        FirstLocal = 0;
        pool.TryPush(this);
    }

    public const int OprMinus = 0;

    public const int OprNot = 1;

    public const int OprLength = 2;

    public const int OprNoUnary = 3;
    public const int NoJump = -1;

    public const int NoRegister = MaxArgA;

    public const int MaxLocalVariables = 200;


    public const int OprAdd = 0;

    public const int OprSub = 1;

    public const int OprMul = 2;

    public const int OprDiv = 3;

    public const int OprMod = 4;

    public const int OprPow = 5;

    public const int OprConcat = 6;

    public const int OprEq = 7;

    public const int OprLT = 8;

    public const int OprLE = 9;

    public const int OprNE = 10;

    public const int OprGT = 11;

    public const int OprGE = 12;

    public const int OprAnd = 13;

    public const int OprOr = 14;

    public const int OprNoBinary = 15;

    public void OpenFunction(int line)
    {
        var newProto = PrototypeBuilder.Get(P.Scanner.Source);
        newProto.Source = P.Scanner.Source;
        newProto.MaxStackSize = 2;
        newProto.LineDefined = line;

        Proto.PrototypeList.Add(newProto);
        var f = Get(P, Proto.PrototypeList[^1]);
        f.Previous = this;
        f.FirstLocal = P.ActiveVariables.Length;
        P.Function = f;

        P.Function.EnterBlock(false);
    }

    public ExprDesc CloseFunction()
    {
        var e = P.Function.Previous!.ExpressionToNextRegister(MakeExpression(Kind.Relocatable, Previous!.EncodeABx(OpCode.Closure, 0, Previous!.Proto.PrototypeList.Length - 1)));
        P.Function.ReturnNone();
        P.Function.LeaveBlock();
        Assert(P.Function.Block == null);
        var f = P.Function;
        P.Function = f.Previous;
        f.Release();
        return e;
    }

    public void EnterBlock(bool isLoop)
    {
        var b = Block.Get(Block, P.ActiveLabels.Length, P.PendingGotos.Length, ActiveVariableCount, false, isLoop);
        Block = b;
        Assert(FreeRegisterCount == ActiveVariableCount);
    }

    public void UndefinedGotoError(Label g)
    {
        if (Scanner.IsReserved(g.Name))
        {
            SemanticError($"<{g.Name}> at line {g.Line} not inside a loop");
        }
        else
        {
            SemanticError($"no visible label '{g.Name}' for <goto> at line {g.Line}");
        }
    }

    public ref LocalVariable LocalVariable(int i)
    {
        var index = P.ActiveVariables[FirstLocal + i];
        return ref Proto.LocalVariablesList[index];
    }

    public void AdjustLocalVariables(int n)
    {
        for (ActiveVariableCount += n; n != 0; n--)
        {
            LocalVariable(ActiveVariableCount - n).StartPc = ((Proto.CodeList.Length));
        }
    }

    public void RemoveLocalVariables(int level)
    {
        for (var i = level; i < ActiveVariableCount; i++)
        {
            LocalVariable(i).EndPc = ((Proto.CodeList.Length));
        }

        P.ActiveVariables.Shrink((P.ActiveVariables.Length - (ActiveVariableCount - level)));
        ActiveVariableCount = level;
    }

    public void MakeLocalVariable(string name)
    {
        var r = Proto.LocalVariablesList.Length;
        Proto.LocalVariablesList.Add(new() { Name = name });
        P.CheckLimit((P.ActiveVariables.Length + 1 - FirstLocal), MaxLocalVariables, "local variables");
        P.ActiveVariables.Add(r);
    }

    public void MakeGoto(string name, int line, int pc)
    {
        P.PendingGotos.Add(new() { Name = name, Line = line, Pc = pc, ActiveVariableCount = ActiveVariableCount });
        FindLabel((P.PendingGotos.Length - 1));
    }

    public int MakeLabel(string name, int line)
    {
        P.ActiveLabels.Add(new() { Name = name, Line = line, Pc = Proto.CodeList.Length, ActiveVariableCount = ActiveVariableCount });
        return (P.ActiveLabels.Length - 1);
    }

    public void CloseGoto(int i, Label l)
    {
        var g = P.PendingGotos[i];
        Assert(g.Name == l.Name);
        if (g.ActiveVariableCount < l.ActiveVariableCount)
        {
            SemanticError($"<goto {g.Name}> at line {g.Line} jumps into the scope of local '{LocalVariable(g.ActiveVariableCount).Name}'");
        }

        PatchList(g.Pc, l.Pc);
        P.PendingGotos.RemoveAtSwapBack(i);
    }

    public int FindLabel(int i)
    {
        var g = P.PendingGotos[i];
        var b = Block;
        foreach (var l in P.ActiveLabels.AsSpan().Slice(b.FirstLabel))
        {
            if (l.Name == g.Name)
            {
                if (g.ActiveVariableCount > l.ActiveVariableCount && (b.HasUpValue || P.ActiveLabels.Length > b.FirstLabel))
                {
                    PatchClose(g.Pc, l.ActiveVariableCount);
                }

                CloseGoto(i, l);
                return 0;
            }
        }

        return 1;
    }

    public void CheckRepeatedLabel(string name)
    {
        foreach (var l in P.ActiveLabels.AsSpan().Slice(Block.FirstLabel))
        {
            if (l.Name == name)
            {
                SemanticError($"label '{name}' already defined on line {l.Line}");
            }
        }
    }

    public void FindGotos(int label)
    {
        for (var i = Block.FirstGoto; i < P.PendingGotos.Length;)
        {
            var l = P.ActiveLabels[label];
            if (P.PendingGotos[i].Name == l.Name)
            {
                CloseGoto(i, l);
            }
            else
            {
                i++;
            }
        }
    }

    public void MoveGotosOut(Block b)
    {
        for (var i = b.FirstGoto; i < P.PendingGotos.Length; i += FindLabel(i))
        {
            if (P.PendingGotos[i].ActiveVariableCount > b.ActiveVariableCount)
            {
                if (b.HasUpValue)
                {
                    PatchClose(P.PendingGotos[i].Pc, b.ActiveVariableCount);
                }

                P.PendingGotos[i].ActiveVariableCount = b.ActiveVariableCount;
            }
        }
    }

    public void LeaveBlock()
    {
        var b = Block;
        if (b.Previous != null && b.HasUpValue) // create a 'jump to here' to close upvalues
        {
            var j = Jump();
            PatchClose(j, b.ActiveVariableCount);
            PatchToHere(j);
        }

        if (b.IsLoop)
        {
            BreakLabel();
        }

        Block = b.Previous!;
        RemoveLocalVariables(b.ActiveVariableCount);
        Assert(b.ActiveVariableCount == ActiveVariableCount);
        FreeRegisterCount = ActiveVariableCount;
        P.ActiveLabels.Shrink(b.FirstLabel);
        if (b.Previous != null) // inner block
        {
            MoveGotosOut(b); // update pending gotos to outer block
        }
        else if (b.FirstGoto < P.PendingGotos.Length) // pending gotos in outer block
        {
            UndefinedGotoError(P.PendingGotos[b.FirstGoto]);
        }

        b.Release();
    }

    public static int Not(int b) => b == 0 ? 1 : 0;


    public static ExprDesc MakeExpression(Kind kind, int info) => new() { F = NoJump, T = NoJump, Kind = kind, Info = info };


    public void SemanticError(string message)
    {
        P.Scanner.Token.T = default;
        P.Scanner.SyntaxError(message);
    }

    public void BreakLabel() => FindGotos(MakeLabel("break", 0));

    [Conditional("DEBUG")]
    public void Unreachable() => Assert(false);


    public ref Instruction Instruction(ExprDesc e) => ref Proto.CodeList[e.Info];


    [Conditional("DEBUG")]
    public void AssertEqual(int a, int b) => Assert(a == b, $"{a} != {b}");


    public int Encode(Instruction i)
    {
        Assert(Proto.CodeList.Length == Proto.LineInfoList.Length);
        DischargeJumpPc();
        Proto.CodeList.Add(i);
        Proto.LineInfoList.Add(P.Scanner.LastLine);
        return Proto.CodeList.Length - 1;
    }

    public void DropLastInstruction()
    {
        Assert(Proto.CodeList.Length == Proto.LineInfoList.Length);
        Proto.CodeList.Pop();
        Proto.LineInfoList.Pop();
    }

    public int EncodeABC(OpCode op, int a, int b, int c)
    {
        return Encode(CreateABC(op, a, b, c));
    }

    public int EncodeABx(OpCode op, int a, int bx) => Encode(CreateABx(op, a, bx));


    public int EncodeAsBx(OpCode op, int a, int sbx) => EncodeABx(op, a, sbx + MaxArgSBx);

    public int EncodeExtraArg(int a) => Encode(CreateAx(OpCode.ExtraArg, a));


    public int EncodeConstant(int r, int constant)
    {
        if (constant <= MaxArgBx)
            return EncodeABx(OpCode.LoadK, r, constant);
        var pc = EncodeABx(OpCode.LoadK, r, 0);
        EncodeExtraArg(constant);
        return pc;
    }

    public ExprDesc EncodeString(string s) => MakeExpression(Kind.Constant, StringConstant(s));


    public void LoadNil(int from, int n)
    {
        if (Proto.CodeList.Length > LastTarget) // no jumps to current position
        {
            ref var previous = ref Proto.CodeList[^1];
            if (previous.OpCode == OpCode.LoadNil)
            {
                var pf = previous.A;
                var pl = previous.A + previous.B;
                var l = from + n - 1;
                if (pf <= from && from <= pl + 1 || from <= pf && pf <= l + 1) // can connect both
                {
                    from = Math.Min(from, pf);
                    l = Math.Max(l, pl);
                    previous.A = from;
                    previous.B = l - from;
                    return;
                }
            }
        }

        EncodeABC(OpCode.LoadNil, from, n - 1, 0);
    }

    public int Jump()
    {
        Assert(IsJumpListWalkable(JumpPc));
        var jumpPc = JumpPc;
        JumpPc = NoJump;
        return Concatenate(EncodeAsBx(OpCode.Jmp, 0, NoJump), jumpPc);
    }

    public void JumpTo(int target)
    {
        PatchList(Jump(), target);
    }

    public void ReturnNone()
    {
        EncodeABC(OpCode.Return, 0, 1, 0);
    }

    public void SetMultipleReturns(ExprDesc e)
    {
        SetReturns(e, MultipleReturns);
    }

    public void Return(ExprDesc e, int resultCount)
    {
        if (e.HasMultipleReturns())
        {
            SetMultipleReturns(e);
            if (e.Kind == Kind.Call && resultCount == 1)
            {
                Instruction(e).OpCode = OpCode.TailCall;
                Assert(Instruction(e).A == ActiveVariableCount);
            }

            EncodeABC(OpCode.Return, ActiveVariableCount, MultipleReturns + 1, 0);
        }
        else if (resultCount == 1)
            EncodeABC(OpCode.Return, ExpressionToAnyRegister(e).Info, 1 + 1, 0);
        else
        {
            ExpressionToNextRegister(e);
            Assert(resultCount == FreeRegisterCount - ActiveVariableCount);
            EncodeABC(OpCode.Return, ActiveVariableCount, resultCount + 1, 0);
        }
    }

    public int ConditionalJump(OpCode op, int a, int b, int c)
    {
        EncodeABC(op, a, b, c);
        return Jump();
    }

    public void FixJump(int pc, int dest)
    {
        Assert(IsJumpListWalkable(pc));
        Assert(dest != NoJump);
        var offset = dest - (pc + 1);
        if (Math.Abs(offset) > MaxArgSBx)
            P.Scanner.SyntaxError("control structure too long");
        Proto.CodeList[pc].SBx = (offset);
    }

    public int Label()
    {
        LastTarget = Proto.CodeList.Length;
        return LastTarget;
    }

    public int Jump(int pc)
    {
        Assert(IsJumpListWalkable(pc));
        var offset = Proto.CodeList[pc].SBx;
        if (offset != NoJump)
            return pc + 1 + offset;
        return NoJump;
    }

    public bool IsJumpListWalkable(int list)
    {
        if (list == NoJump)
            return true;
        if (list < 0 || list >= Proto.CodeList.Length)
            return false;
        var offset = Proto.CodeList[list].SBx;
        return offset == NoJump || IsJumpListWalkable(list + 1 + offset);
    }

    public ref Instruction JumpControl(int pc)
    {
        if (pc >= 1 && TestTMode(Proto.CodeList[pc - 1].OpCode))
            return ref Proto.CodeList[pc - 1];
        return ref Proto.CodeList[pc];
    }

    public bool NeedValue(int list)
    {
        Assert(IsJumpListWalkable(list));
        for (; list != NoJump; list = Jump(list))
        {
            if (JumpControl(list).OpCode != OpCode.TestSet)
                return true;
        }

        return false;
    }

    public bool PatchTestRegister(int node, int register)
    {
        ref var i = ref JumpControl(node);
        if (i.OpCode != OpCode.TestSet)
            return false;
        if (register != NoRegister && register != i.B)
            i.A = register;
        else
            i = CreateABC(OpCode.Test, i.B, 0, i.C);
        return true;
    }

    public void RemoveValues(int list)
    {
        Assert(IsJumpListWalkable(list));
        for (; list != NoJump; list = Jump(list))
        {
            PatchTestRegister(list, NoRegister);
        }
    }

    public void PatchListHelper(int list, int target, int register, int defaultTarget)
    {
        Assert(IsJumpListWalkable(list));

        while (list != NoJump)
        {
            var next = Jump(list);
            if (PatchTestRegister(list, register))
            {
                FixJump(list, target);
            }
            else
            {
                FixJump(list, defaultTarget);
            }

            list = next;
        }
    }

    public void DischargeJumpPc()
    {
        Assert(IsJumpListWalkable(JumpPc));
        PatchListHelper(JumpPc, Proto.CodeList.Length, NoRegister, Proto.CodeList.Length);
        JumpPc = NoJump;
    }

    public void PatchList(int list, int target)
    {
        if (target == Proto.CodeList.Length)
        {
            PatchToHere(list);
        }
        else
        {
            Assert(target < Proto.CodeList.Length);
            PatchListHelper(list, target, NoRegister, target);
        }
    }

    public void PatchClose(int list, int level)
    {
        Assert(IsJumpListWalkable(list));
        level++;
        for (int next; list != NoJump; list = next)
        {
            next = Jump(list);
            Assert(Proto.CodeList[list].OpCode == OpCode.Jmp && Proto.CodeList[list].A == 0 || Proto.CodeList[list].A >= level);
            Proto.CodeList[list].A = level;
        }
    }

    public void PatchToHere(int list)
    {
        Assert(IsJumpListWalkable(list));
        Assert(IsJumpListWalkable(JumpPc));
        Label();
        JumpPc = Concatenate(JumpPc, list);
        Assert(IsJumpListWalkable(JumpPc));
    }

    public int Concatenate(int l1, int l2)
    {
        Assert(IsJumpListWalkable(l1));

        if (l2 == NoJump) return l1;
        if (l1 == NoJump)
        {
            return l2;
        }

        var list = l1;
        for (var next = Jump(list); next != NoJump;)
        {
            (list, next) = (next, Jump(next));
        }

        FixJump(list, l2);
        return l1;
    }

    public int AddConstant(LuaValue k, LuaValue v)
    {
        if (ConstantLookup.TryGetValue(k, out var index) && Proto.ConstantsList[index] == v)
        {
            return index;
        }

        index = Proto.ConstantsList.Length;
        ConstantLookup[k] = index;
        Proto.ConstantsList.Add(v);
        return index;
    }

    public unsafe int NumberConstant(double n)
    {
        if (n == 0.0 || double.IsNaN(n))
        {
            return AddConstant(*(long*)&n, n);
        }

        return AddConstant(n, n);
    }

    public void CheckStack(int n)
    {
        n += FreeRegisterCount;
        if (n >= MaxStack)
        {
            P.Scanner.SyntaxError("function or expression too complex");
        }
        else if (n > Proto.MaxStackSize)
        {
            Proto.MaxStackSize = n;
        }
    }

    public void ReserveRegisters(int n)
    {
        CheckStack(n);
        FreeRegisterCount += n;
    }

    public void FreeRegister(int r)
    {
        if (!IsConstant(r) && r >= ActiveVariableCount)
        {
            FreeRegisterCount--;
            AssertEqual(r, FreeRegisterCount);
        }
    }

    public void FreeExpression(ExprDesc e)
    {
        if (e.Kind == Kind.NonRelocatable)
        {
            FreeRegister(e.Info);
        }
    }

    public int StringConstant(string s)
    {
        return AddConstant(s, s);
    }

    public int BooleanConstant(bool b)
    {
        return AddConstant(b, b);
    }

    public int NilConstant()
    {
        return AddConstant(default, default);
    }

    public void SetReturns(ExprDesc e, int resultCount)
    {
        if (e.Kind == Kind.Call)
        {
            Instruction(e).C = resultCount + 1;
        }
        else if (e.Kind == Kind.VarArg)
        {
            Instruction(e).B = resultCount + 1;
            Instruction(e).A = FreeRegisterCount;
            ReserveRegisters(1);
        }
    }

    public ExprDesc SetReturn(ExprDesc e)
    {
        if (e.Kind == Kind.Call)
        {
            e.Kind = Kind.NonRelocatable;
            e.Info = Instruction(e).A;
        }
        else if (e.Kind == Kind.VarArg)
        {
            Instruction(e).B = 2;
            e.Kind = Kind.Relocatable;
        }

        return e;
    }

    public ExprDesc DischargeVariables(ExprDesc e)
    {
        switch (e.Kind)
        {
            case Kind.Local:
                e.Kind = Kind.NonRelocatable;
                break;
            case Kind.UpValue:
                e.Kind = Kind.Relocatable;
                e.Info = EncodeABC(OpCode.GetUpVal, 0, e.Info, 0);
                break;
            case Kind.Indexed:
                FreeRegister(e.Index);
            {
                if (e.TableType == Kind.Local)
                {
                    FreeRegister(e.Table);
                    e.Kind = Kind.Relocatable;
                    e.Info = EncodeABC(OpCode.GetTable, 0, e.Table, e.Index);
                }
                else
                {
                    e.Kind = Kind.Relocatable;
                    e.Info = EncodeABC(OpCode.GetTabUp, 0, e.Table, e.Index);
                }
            }
                break;
            case Kind.VarArg:
            case Kind.Call:
                e = SetReturn(e);
                break;
        }

        return e;
    }

    public ExprDesc DischargeToRegister(ExprDesc e, int r)
    {
        e = DischargeVariables(e);
        switch (e.Kind)
        {
            case Kind.Nil:
                LoadNil(r, 1);
                break;
            case Kind.False:
                EncodeABC(OpCode.LoadBool, r, 0, 0);
                break;
            case Kind.True:
                EncodeABC(OpCode.LoadBool, r, 1, 0);
                break;
            case Kind.Constant:
                EncodeConstant(r, e.Info);
                break;
            case Kind.Number:
                EncodeConstant(r, NumberConstant(e.Value));
                break;
            case Kind.Relocatable:
                Instruction(e).A = r;
                break;
            case Kind.NonRelocatable:
                if (r != e.Info)
                {
                    EncodeABC(OpCode.Move, r, e.Info, 0);
                }

                break;
            default:
                Assert(e.Kind == Kind.Void || e.Kind == Kind.Jump);
                return e;
        }

        e.Kind = Kind.NonRelocatable;
        e.Info = r;
        return e;
    }

    public ExprDesc DischargeToAnyRegister(ExprDesc e)
    {
        if (e.Kind != Kind.NonRelocatable)
        {
            ReserveRegisters(1);
            e = DischargeToRegister(e, FreeRegisterCount - 1);
        }

        return e;
    }

    public int EncodeLabel(int a, int b, int jump)
    {
        Label();
        return EncodeABC(OpCode.LoadBool, a, b, jump);
    }

    public ExprDesc ExpressionToRegister(ExprDesc e, int r)
    {
        e = DischargeToRegister(e, r);
        if (e.Kind == Kind.Jump)
        {
            e.T = Concatenate(e.T, e.Info);
        }

        if (e.HasJumps())
        {
            int loadFalse = NoJump;
            int loadTrue = NoJump;
            if (NeedValue(e.T) || NeedValue(e.F))
            {
                int jump = NoJump;
                if (e.Kind != Kind.Jump) jump = Jump();
                loadFalse = EncodeLabel(r, 0, 1);
                loadTrue = EncodeLabel(r, 1, 0);
                PatchToHere(jump);
            }

            int end = Label();
            PatchListHelper(e.F, end, r, loadFalse);
            PatchListHelper(e.T, end, r, loadTrue);
        }

        e.F = e.T = NoJump;
        e.Info = r;
        e.Kind = Kind.NonRelocatable;
        return e;
    }

    public ExprDesc ExpressionToNextRegister(ExprDesc e)
    {
        e = DischargeVariables(e);
        FreeExpression(e);
        ReserveRegisters(1);
        return ExpressionToRegister(e, FreeRegisterCount - 1);
    }

    public ExprDesc ExpressionToAnyRegister(ExprDesc e)
    {
        e = DischargeVariables(e);
        if (e.Kind == Kind.NonRelocatable)
        {
            if (!e.HasJumps())
                return e;
            if (e.Info >= ActiveVariableCount)
            {
                return ExpressionToRegister(e, e.Info);
            }
        }

        return ExpressionToNextRegister(e);
    }

    public ExprDesc ExpressionToAnyRegisterOrUpValue(ExprDesc e)
    {
        if (e.Kind != Kind.UpValue || e.HasJumps())
        {
            e = ExpressionToAnyRegister(e);
        }

        return e;
    }

    public ExprDesc ExpressionToValue(ExprDesc e)
    {
        if (e.HasJumps()) return ExpressionToAnyRegister(e);
        return DischargeVariables(e);
    }

    public (ExprDesc, int) ExpressionToRegisterOrConstant(ExprDesc e)
    {
        e = ExpressionToValue(e);
        switch (e.Kind)
        {
            case Kind.True:
            case Kind.False:
                if (Proto.ConstantsList.Length <= MaxIndexRK)
                {
                    e.Info = BooleanConstant(e.Kind == Kind.True);
                    e.Kind = Kind.Constant;
                    return (e, AsConstant(e.Info));
                }

                break;
            case Kind.Nil:
                if (Proto.ConstantsList.Length <= MaxIndexRK)
                {
                    e.Info = NilConstant();
                    e.Kind = Kind.Constant;
                    return (e, AsConstant(e.Info));
                }

                break;
            case Kind.Number:
                e.Info = NumberConstant(e.Value);
                e.Kind = Kind.Constant;
                goto case Kind.Constant;
            case Kind.Constant:
                if (e.Info <= MaxIndexRK)
                {
                    return (e, AsConstant(e.Info));
                }

                break;
        }

        e = ExpressionToAnyRegister(e);
        return (e, e.Info);
    }

    public void StoreVariable(ExprDesc v, ExprDesc e)
    {
        switch (v.Kind)
        {
            case Kind.Local:
                FreeExpression(e);
                ExpressionToRegister(e, v.Info);
                return;
            case Kind.UpValue:
                e = ExpressionToAnyRegister(e);
                EncodeABC(OpCode.SetUpVal, e.Info, v.Info, 0);
                break;
            case Kind.Indexed:
                var r = 0;
                (e, r) = ExpressionToRegisterOrConstant(e);
                EncodeABC(v.TableType == Kind.Local ? OpCode.SetTable : OpCode.SetTabUp, v.Table, v.Index, r);

                break;
            default:
                Unreachable();
                break;
        }

        FreeExpression(e);
    }

    public ExprDesc Self(ExprDesc e, ExprDesc key)
    {
        e = ExpressionToAnyRegister(e);
        var r = e.Info;
        FreeExpression(e);
        var result = new ExprDesc { Info = FreeRegisterCount, Kind = Kind.NonRelocatable }; // base register for opSelf
        ReserveRegisters(2); // function and 'self' produced by opSelf
        (key, var k) = ExpressionToRegisterOrConstant(key);
        EncodeABC(OpCode.Self, result.Info, r, k);
        FreeExpression(key);
        return result;
    }

    public void InvertJump(int pc)
    {
        ref var i = ref JumpControl(pc);
        Assert(TestTMode(i.OpCode) && i.OpCode is not (OpCode.TestSet or OpCode.Test));
        i.A = Not(i.A);
    }

    public int JumpOnCondition(ExprDesc e, int cond)
    {
        if (e.Kind == Kind.Relocatable)
        {
            var i = Instruction(e);
            if (i.OpCode == OpCode.Not)
            {
                DropLastInstruction(); // remove previous opNot
                return ConditionalJump(OpCode.Test, i.B, 0, Not(cond));
            }
        }

        e = DischargeToAnyRegister(e);
        FreeExpression(e);
        return ConditionalJump(OpCode.TestSet, NoRegister, e.Info, cond);
    }

    public ExprDesc GoIfTrue(ExprDesc e)
    {
        var pc = NoJump;
        e = DischargeVariables(e);
        switch (e.Kind)
        {
            case Kind.Jump:
                InvertJump(e.Info);
                pc = e.Info;
                break;
            case Kind.Constant:
            case Kind.Number:
            case Kind.True:
                break;
            default:
                pc = JumpOnCondition(e, 0);
                break;
        }

        e.F = Concatenate(e.F, pc);
        PatchToHere(e.T);
        e.T = NoJump;
        return e;
    }

    public ExprDesc GoIfFalse(ExprDesc e)
    {
        var pc = NoJump;
        e = DischargeVariables(e);
        switch (e.Kind)
        {
            case Kind.Jump:
                pc = e.Info;
                break;
            case Kind.Nil:
            case Kind.False:
                break;
            default:
                pc = JumpOnCondition(e, 1);
                break;
        }

        e.T = Concatenate(e.T, pc);
        PatchToHere(e.F);
        e.F = NoJump;
        return e;
    }

    public ExprDesc EncodeNot(ExprDesc e)
    {
        e = DischargeVariables(e);
        switch (e.Kind)
        {
            case Kind.Nil:
            case Kind.False:
                e.Kind = Kind.True;
                break;
            case Kind.Constant:
            case Kind.Number:
            case Kind.True:
                e.Kind = Kind.False;
                break;
            case Kind.Jump:
                InvertJump(e.Info);
                break;
            case Kind.Relocatable:
            case Kind.NonRelocatable:
                e = DischargeToAnyRegister(e);
                FreeExpression(e);
                e.Info = EncodeABC(OpCode.Not, 0, e.Info, 0);
                e.Kind = Kind.Relocatable;
                break;
            default:
                Unreachable();
                break;
        }

        (e.T, e.F) = (e.F, e.T);
        RemoveValues(e.F);
        RemoveValues(e.T);
        return e;
    }

    public ExprDesc Indexed(ExprDesc t, ExprDesc k)
    {
        Assert(!t.HasJumps());
        var r = MakeExpression(Kind.Indexed, 0);
        r.Table = t.Info;
        var (_, i) = ExpressionToRegisterOrConstant(k);
        r.Index = i;
        if (t.Kind == Kind.UpValue)
            r.TableType = Kind.UpValue;
        else
        {
            Assert(t.Kind == Kind.NonRelocatable || t.Kind == Kind.Local);
            r.TableType = Kind.Local;
        }

        return r;
    }


    private static double Arith(OpCode op, double v1, double v2)
    {
        switch (op)
        {
            case OpCode.Add:
                return v1 + v2;
            case OpCode.Sub:
                return v1 - v2;
            case OpCode.Mul:
                return v1 * v2;
            case OpCode.Div:
                return v1 / v2;
            case OpCode.Mod:
                return v1 - Math.Floor(v1 / v2) * v2;
            case OpCode.Pow:
                return Math.Pow(v1, v2);
            case OpCode.Unm:
                return -v1;
        }

        throw new("not an arithmetic op code (" + op + ")");
    }

    public static (ExprDesc, bool) FoldConstants(OpCode op, ExprDesc e1, ExprDesc e2)
    {
        if (!e1.IsNumeral() || !e2.IsNumeral())
            return (e1, false);
        if ((op == OpCode.Div || op == OpCode.Mod) && e2.Value == 0.0)
            return (e1, false);
        e1.Value = Arith(op, e1.Value, e2.Value);
        return (e1, true);
    }

    public ExprDesc EncodeArithmetic(OpCode op, ExprDesc e1, ExprDesc e2, int line)
    {
        var (e, folded) = FoldConstants(op, e1, e2);
        if (folded)
            return e;
        var o2 = 0;
        if (op != OpCode.Unm && op != OpCode.Len)
        {
            (e2, o2) = ExpressionToRegisterOrConstant(e2);
        }

        (e1, var o1) = ExpressionToRegisterOrConstant(e1);
        if (o1 > o2)
        {
            FreeExpression(e1);
            FreeExpression(e2);
        }
        else
        {
            FreeExpression(e2);
            FreeExpression(e1);
        }

        e1.Info = EncodeABC(op, 0, o1, o2);
        e1.Kind = Kind.Relocatable;
        FixLine(line);
        return e1;
    }

    public ExprDesc Prefix(int op, ExprDesc e, int line)
    {
        switch (op)
        {
            case OprMinus:
                if (e.IsNumeral())
                {
                    e.Value = -e.Value;
                    return e;
                }

                return EncodeArithmetic(OpCode.Unm, ExpressionToAnyRegister(e), MakeExpression(Kind.Number, 0), line);
            case OprNot:
                return EncodeNot(e);
            case OprLength:
                return EncodeArithmetic(OpCode.Len, ExpressionToAnyRegister(e), MakeExpression(Kind.Number, 0), line);
        }

        throw new("unreachable");
    }

    public ExprDesc Infix(int op, ExprDesc e)
    {
        switch (op)
        {
            case OprAnd:
                e = GoIfTrue(e);
                break;
            case OprOr:
                e = GoIfFalse(e);
                break;
            case OprConcat:
                e = ExpressionToNextRegister(e);
                break;
            case OprAdd:
            case OprSub:
            case OprMul:
            case OprDiv:
            case OprMod:
            case OprPow:
                if (!e.IsNumeral())
                    (e, _) = ExpressionToRegisterOrConstant(e);
                break;
            default:
                (e, _) = ExpressionToRegisterOrConstant(e);
                break;
        }

        return e;
    }

    public ExprDesc EncodeComparison(OpCode op, int cond, ExprDesc e1, ExprDesc e2)
    {
        (e1, var o1) = ExpressionToRegisterOrConstant(e1);
        (e2, var o2) = ExpressionToRegisterOrConstant(e2);
        FreeExpression(e2);
        FreeExpression(e1);
        if (cond == 0 && op != OpCode.Eq)
        {
            (o1, o2, cond) = (o2, o1, 1);
        }

        return MakeExpression(Kind.Jump, ConditionalJump(op, cond, o1, o2));
    }

    public ExprDesc Postfix(int op, ExprDesc e1, ExprDesc e2, int line)
    {
        switch (op)
        {
            case OprAnd:
                Assert(e1.T == NoJump);
                e2 = DischargeVariables(e2);
                e2.F = Concatenate(e2.F, e1.F);
                return e2;
            case OprOr:
                Assert(e1.F == NoJump);
                e2 = DischargeVariables(e2);
                e2.T = Concatenate(e2.T, e1.T);
                return e2;
            case OprConcat:
                e2 = ExpressionToValue(e2);
                if (e2.Kind == Kind.Relocatable && Instruction(e2).OpCode == OpCode.Concat)
                {
                    Assert(e1.Info == Instruction(e2).B - 1);
                    FreeExpression(e1);
                    Instruction(e2).B = (e1.Info);
                    return MakeExpression(Kind.Relocatable, e2.Info);
                }

                return EncodeArithmetic(OpCode.Concat, e1, ExpressionToNextRegister(e2), line);
            case OprAdd:
            case OprSub:
            case OprMul:
            case OprDiv:
            case OprMod:
            case OprPow:
                return EncodeArithmetic((OpCode)(op - OprAdd + (byte)OpCode.Add), e1, e2, line);
            case OprEq:
            case OprLT:
            case OprLE:
                return EncodeComparison((OpCode)(op - OprEq + (byte)OpCode.Eq), 1, e1, e2);
            case OprNE:
            case OprGT:
            case OprGE:
                return EncodeComparison((OpCode)(op - OprNE + (byte)OpCode.Eq), 0, e1, e2);
            default:
                throw new("unreachable");
        }
    }

    public void FixLine(int line) => Proto.LineInfoList[Proto.CodeList.Length - 1] = line;


    public void SetList(int @base, int elementCount, int storeCount)
    {
        Assert(storeCount != 0);
        if (storeCount == MultipleReturns)
        {
            storeCount = 0;
        }

        var c = (elementCount - 1) / ListItemsPerFlush + 1;
        if (c <= MaxArgC)
        {
            EncodeABC(OpCode.SetList, @base, storeCount, c);
        }
        else if (c <= MaxArgAx)
        {
            EncodeABC(OpCode.SetList, @base, storeCount, 0);
            EncodeExtraArg(c);
        }
        else
        {
            P.Scanner.SyntaxError("constructor too long");
        }

        FreeRegisterCount = @base + 1;
    }

    public unsafe void CheckConflict(AssignmentTarget tv, ExprDesc e)
    {
        var extra = FreeRegisterCount;
        var conflict = false;
        var t = &tv;
        while (t != null)
        {
            ref var d = ref t->Description;
            if (d.Kind == Kind.Indexed)
            {
                if (d.TableType == e.Kind && d.Table == e.Info)
                {
                    conflict = true;
                    d.Table = extra;
                    d.TableType = Kind.Local;
                }

                if (e.Kind == Kind.Local && d.Index == e.Info)
                {
                    conflict = true;
                    d.Index = extra;
                }
            }

            t = t->Previous;
        }

        if (conflict)
        {
            if (e.Kind == Kind.Local)
            {
                EncodeABC(OpCode.Move, extra, e.Info, 0);
            }
            else
            {
                EncodeABC(OpCode.GetUpVal, extra, e.Info, 0);
            }

            ReserveRegisters(1);
        }
    }

    public void AdjustAssignment(int variableCount, int expressionCount, ExprDesc e)
    {
        var extra = variableCount - expressionCount;
        if (e.HasMultipleReturns())
        {
            extra++;
            if (extra < 0)
            {
                extra = 0;
            }

            SetReturns(e, extra);
            if (extra > 1)
            {
                ReserveRegisters(extra - 1);
            }
        }
        else
        {
            if (expressionCount > 0)
            {
                ExpressionToNextRegister(e);
            }

            if (extra > 0)
            {
                var r = FreeRegisterCount;
                ReserveRegisters(extra);
                LoadNil(r, extra);
            }
        }
    }

    public int MakeUpValue(string name, ExprDesc e)
    {
        P.CheckLimit(Proto.UpValuesList.Length + 1, MaxUpValue, "upvalues");
        Proto.UpValuesList.Add(new() { Name = name, IsLocal = e.Kind == Kind.Local, Index = e.Info });
        return Proto.UpValuesList.Length - 1;
    }

    public static (ExprDesc, bool) SingleVariableHelper(Function? f, string name, bool b)
    {
        static Block owningBlock(Block b1, int level)
        {
            while (b1.ActiveVariableCount > level)
            {
                b1 = b1.Previous!;
            }

            return b1;
        }

        ;

        static (int, bool) find(Function f, string name)
        {
            for (var i = f.ActiveVariableCount - 1; i >= 0; i--)
            {
                if (name == f.LocalVariable(i).Name)
                {
                    return (i, true);
                }
            }

            return (0, false);
        }

        ;

        static (int, bool) findUpValue(Function f, string name)
        {
            for (var i = 0; i < f.Proto.UpValuesList.Length; i++)
            {
                if (f.Proto.UpValuesList[i].Name == name)
                {
                    return (i, true);
                }
            }

            return (0, false);
        }

        ;

        if (f == null)
        {
            return default;
        }

        var (v, found) = find(f, name);
        if (found)
        {
            var e = MakeExpression(Kind.Local, v);
            if (!b)
            {
                owningBlock(f.Block, v).HasUpValue = true;
            }

            return (e, true);
        }

        (v, found) = findUpValue(f, name);
        if (found)
        {
            return (MakeExpression(Kind.UpValue, v), true);
        }

        {
            (var e, found) = SingleVariableHelper(f.Previous, name, false);
            if (!found)
            {
                return (e, found);
            }

            return (MakeExpression(Kind.UpValue, f.MakeUpValue(name, e)), true);
        }
    }

    public ExprDesc SingleVariable(string name)
    {
        var (e, found) = SingleVariableHelper(this, name, true);
        if (!found)
        {
            (e, found) = SingleVariableHelper(this, "_ENV", true);
            Assert(found && (e.Kind == Kind.Local || e.Kind == Kind.UpValue));
            e = Indexed(e, EncodeString(name));
        }

        return e;
    }

    public (int pc, ExprDesc t) OpenConstructor()
    {
        var pc = EncodeABC(OpCode.NewTable, 0, 0, 0);
        var t = ExpressionToNextRegister(MakeExpression(Kind.Relocatable, pc));
        return (pc, t);
    }

    public void FlushFieldToConstructor(int tableRegister, int freeRegisterCount, ExprDesc k, Func<ExprDesc> v)
    {
        (_, var rk) = ExpressionToRegisterOrConstant(k);
        (_, var rv) = ExpressionToRegisterOrConstant(v());
        EncodeABC(OpCode.SetTable, tableRegister, rk, rv);
        FreeRegisterCount = freeRegisterCount;
    }

    public int FlushToConstructor(int tableRegister, int pending, int arrayCount, ExprDesc e)
    {
        ExpressionToNextRegister(e);
        if (pending == ListItemsPerFlush)
        {
            SetList(tableRegister, arrayCount, ListItemsPerFlush);
            pending = 0;
        }

        return pending;
    }

    public void CloseConstructor(int pc, int tableRegister, int pending, int arrayCount, int hashCount, ExprDesc e)
    {
        if (pending != 0)
        {
            if (e.HasMultipleReturns())
            {
                SetMultipleReturns(e);
                SetList(tableRegister, arrayCount, MultipleReturns);
                arrayCount--;
            }
            else
            {
                if (e.Kind != Kind.Void)
                {
                    ExpressionToNextRegister(e);
                }

                SetList(tableRegister, arrayCount, pending);
            }
        }

        Proto.CodeList[pc].B = (((arrayCount)));
        Proto.CodeList[pc].C = (((hashCount)));
    }

    public int OpenForBody(int @base, int n, bool isNumeric)
    {
        var prep = isNumeric ? EncodeAsBx(OpCode.ForPrep, @base, NoJump) : Jump();
        EnterBlock(false);
        AdjustLocalVariables(n);
        ReserveRegisters(n);
        return prep;
    }

    public void CloseForBody(int prep, int @base, int line, int n, bool isNumeric)
    {
        LeaveBlock();
        PatchToHere(prep);
        int end;
        if (isNumeric)
        {
            end = EncodeAsBx(OpCode.ForLoop, @base, NoJump);
        }
        else
        {
            EncodeABC(OpCode.TForCall, @base, 0, n);
            FixLine(line);
            end = EncodeAsBx(OpCode.TForLoop, @base + 2, NoJump);
        }

        PatchList(end, prep + 1);
        FixLine(line);
    }

    public void OpenMainFunction()
    {
        EnterBlock(false);
        MakeUpValue("_ENV", MakeExpression(Kind.Local, 0));
    }

    public Function CloseMainFunction()
    {
        ReturnNone();
        LeaveBlock();
        Assert(Block == null);
        return Previous!;
    }
}