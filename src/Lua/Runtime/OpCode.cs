namespace Lua.Runtime;

public enum OpCode : byte
{
    Move, // A B     R(A) := R(B)
    LoadK, // A Bx    R(A) := Kst(Bx)
    LoadKX, // A       R(A) := Kst(extra arg)
    LoadBool, // A B C   R(A) := (Bool)B; if (C) pc++
    LoadNil, // A B     R(A), R(A+1), ..., R(A+B) := nil

    GetUpVal, // A B     R(A) := UpValue[B]
    GetTabUp, // A B C   R(A) := UpValue[B][RK(C)]
    GetTable, // A B C   R(A) := R(B)[RK(C)]

    SetTabUp, // A B C   UpValue[A][RK(B)] := RK(C)
    SetUpVal, // A B     UpValue[B] := R(A)
    SetTable, // A B C   R(A)[RK(B)] := RK(C)

    NewTable, // A B C   R(A) := {} (size = B,C)

    Self, // A B C   R(A+1) := R(B); R(A) := R(B)[RK(C)]

    Add, // A B C   R(A) := RK(B) + RK(C)
    Sub, // A B C   R(A) := RK(B) - RK(C)
    Mul, // A B C   R(A) := RK(B) * RK(C)
    Div, // A B C   R(A) := RK(B) / RK(C)
    Mod, // A B C   R(A) := RK(B) % RK(C)
    Pow, // A B C   R(A) := RK(B) ^ RK(C)
    Unm, // A B     R(A) := -R(B)
    Not, // A B     R(A) := not R(B)
    Len, // A B     R(A) := length of R(B)

    Concat, // A B C   R(A) := R(B).. ... ..R(C)

    Jmp, // A sBx   pc+=sBx; if (A) close all upvalues >= R(A - 1)
    Eq, // A B C   if ((RK(B) == RK(C)) ~= A) then pc++
    Lt, // A B C   if ((RK(B) <  RK(C)) ~= A) then pc++
    Le, // A B C   if ((RK(B) <= RK(C)) ~= A) then pc++

    Test, // A C     if not (R(A) <=> C) then pc++
    TestSet, // A B C   if (R(B) <=> C) then R(A) := R(B) else pc++

    Call, // A B C   R(A), ... ,R(A+C-2) := R(A)(R(A+1), ... ,R(A+B-1))
    TailCall, // A B C   return R(A)(R(A+1), ... ,R(A+B-1))
    Return, // A B     return R(A), ... ,R(A+B-2)      (see note)

    ForLoop, // A sBx   R(A)+=R(A+2);

    //         if R(A) <?= R(A+1) then { pc+=sBx; R(A+3)=R(A) }
    ForPrep, // A sBx   R(A)-=R(A+2); pc+=sBx

    TForCall, // A C     R(A+3), ... ,R(A+2+C) := R(A)(R(A+1), R(A+2));
    TForLoop, // A sBx   if R(A+1) ~= nil then { R(A)=R(A+1); pc += sBx }

    SetList, // A B C   R(A)[(C-1)*FPF+i] := R(A+i), 1 <= i <= B

    Closure, // A Bx    R(A) := closure(KPROTO[Bx])

    VarArg, // A B     R(A), R(A+1), ..., R(A+B-2) = vararg

    ExtraArg // Ax      extra (larger) argument for previous opcode
}