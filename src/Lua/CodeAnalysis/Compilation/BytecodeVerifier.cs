using System.Diagnostics.CodeAnalysis;
using Lua.Runtime;

namespace Lua.CodeAnalysis.Compilation;

// Loaded chunks cannot share the compiler's operand/bounds guarantees. Verify
// once at the input boundary so the VM can keep its unchecked register accesses.
readonly ref struct BytecodeVerifier(Prototype prototype, ReadOnlySpan<char> name)
{
    readonly ReadOnlySpan<char> chunkName = name;

    public void Verify()
    {
        if (prototype.ParameterCount > prototype.MaxStackSize || prototype.Code.IsEmpty)
            Invalid("function header");

        var code = prototype.Code;
        for (var pc = 0; pc < code.Length; pc++)
        {
            var instruction = code[pc];
            var a = instruction.A;
            var b = instruction.B;
            var c = instruction.C;
            switch (instruction.OpCode)
            {
                case OpCode.Move:
                case OpCode.Unm:
                case OpCode.Not:
                case OpCode.Len:
                    Register(a);
                    Register(b);
                    break;
                case OpCode.LoadK:
                    Register(a);
                    Constant(instruction.Bx);
                    break;
                case OpCode.LoadKX:
                    Register(a);
                    Constant(ExtraArgument(pc));
                    Target(pc + 2);
                    continue;
                case OpCode.LoadBool:
                    Register(a);
                    Target(pc + (c == 0 ? 1 : 2));
                    continue;
                case OpCode.LoadNil:
                    Registers(a, b + 1);
                    break;
                case OpCode.GetUpVal:
                case OpCode.SetUpVal:
                    Register(a);
                    UpValue(b);
                    break;
                case OpCode.GetTabUp:
                    Register(a);
                    UpValue(b);
                    RegisterOrConstant(c);
                    break;
                case OpCode.GetTable:
                    Register(a);
                    Register(b);
                    RegisterOrConstant(c);
                    break;
                case OpCode.SetTabUp:
                    UpValue(a);
                    RegisterOrConstant(b);
                    RegisterOrConstant(c);
                    break;
                case OpCode.SetTable:
                    Register(a);
                    RegisterOrConstant(b);
                    RegisterOrConstant(c);
                    break;
                case OpCode.NewTable:
                    Register(a);
                    break;
                case OpCode.Self:
                    Registers(a, 2);
                    Register(b);
                    RegisterOrConstant(c);
                    break;
                case OpCode.Add:
                case OpCode.Sub:
                case OpCode.Mul:
                case OpCode.Div:
                case OpCode.Mod:
                case OpCode.Pow:
                    Register(a);
                    RegisterOrConstant(b);
                    RegisterOrConstant(c);
                    break;
                case OpCode.Concat:
                    Register(a);
                    if (b >= c)
                        Invalid("concatenation range");
                    Registers(b, c - b + 1);
                    break;
                case OpCode.Jmp:
                    if (a != 0)
                        Register(a - 1);
                    Target(pc + 1 + instruction.SBx);
                    continue;
                case OpCode.Eq:
                case OpCode.Lt:
                case OpCode.Le:
                    Flag(a);
                    RegisterOrConstant(b);
                    RegisterOrConstant(c);
                    Target(pc + 2);
                    break;
                case OpCode.Test:
                    Register(a);
                    Flag(c);
                    Target(pc + 2);
                    break;
                case OpCode.TestSet:
                    Register(a);
                    Register(b);
                    Flag(c);
                    Target(pc + 2);
                    break;
                case OpCode.Call:
                case OpCode.TailCall:
                    Register(a);
                    if (b != 0)
                        Registers(a, b);
                    if (instruction.OpCode == OpCode.TailCall)
                        continue;
                    if (c != 0)
                        Registers(a, c - 1);
                    break;
                case OpCode.Return:
                    Registers(a, b == 0 ? 0 : b - 1);
                    continue;
                case OpCode.ForLoop:
                case OpCode.ForPrep:
                    Registers(a, 4);
                    Target(pc + 1 + instruction.SBx);
                    if (instruction.OpCode == OpCode.ForPrep)
                        continue;
                    break;
                case OpCode.TForCall:
                    Registers(a, 3 + c);
                    break;
                case OpCode.TForLoop:
                    Registers(a, 2);
                    Target(pc + 1 + instruction.SBx);
                    break;
                case OpCode.SetList:
                    Registers(a, b + 1);
                    var block = c == 0 ? ExtraArgument(pc) : c;
                    if (block == 0 || block > (int.MaxValue - LuaStack.MaxCapacity) / 50 + 1)
                        Invalid("table constructor block");
                    if (c == 0)
                    {
                        Target(pc + 2);
                        continue;
                    }
                    break;
                case OpCode.Closure:
                    Register(a);
                    if ((uint)instruction.Bx >= (uint)prototype.ChildPrototypes.Length)
                        Invalid("child prototype index");
                    break;
                case OpCode.VarArg:
                    if (!prototype.HasVariableArguments)
                        Invalid("vararg instruction in fixed-argument function");
                    Registers(a, b == 0 ? 0 : b - 1);
                    break;
                case OpCode.ExtraArg:
                    if (
                        pc == 0
                        || !(
                            code[pc - 1].OpCode == OpCode.LoadKX
                            || (code[pc - 1].OpCode == OpCode.SetList && code[pc - 1].C == 0)
                        )
                    )
                        Invalid("unexpected extra argument");
                    continue;
                default:
                    Invalid("opcode");
                    break;
            }

            Target(pc + 1);
        }

        foreach (var child in prototype.ChildPrototypes)
        {
            foreach (var upValue in child.UpValues)
            {
                if (upValue.IsLocal)
                    Register(upValue.Index);
                else
                    UpValue(upValue.Index);
            }
        }
    }

    void Register(int index) => Registers(index, 1);

    void Registers(int start, int count)
    {
        if (
            (uint)start > (uint)prototype.MaxStackSize
            || (uint)count > (uint)(prototype.MaxStackSize - start)
        )
            Invalid("register range");
    }

    void Constant(int index)
    {
        if ((uint)index >= (uint)prototype.Constants.Length)
            Invalid("constant index");
    }

    void UpValue(int index)
    {
        if ((uint)index >= (uint)prototype.UpValues.Length)
            Invalid("upvalue index");
    }

    void RegisterOrConstant(int operand)
    {
        if (Instruction.IsConstant(operand))
            Constant(Instruction.ConstantIndex(operand));
        else
            Register(operand);
    }

    void Target(int pc)
    {
        if ((uint)pc >= (uint)prototype.Code.Length || prototype.Code[pc].OpCode == OpCode.ExtraArg)
            Invalid("instruction target");
    }

    int ExtraArgument(int pc)
    {
        if (pc + 1 >= prototype.Code.Length || prototype.Code[pc + 1].OpCode != OpCode.ExtraArg)
            Invalid("missing extra argument");
        return prototype.Code[pc + 1].Ax;
    }

    void Flag(int value)
    {
        if (value > 1)
            Invalid("boolean operand");
    }

    [DoesNotReturn]
    void Invalid(string reason) =>
        throw new LuaUndumpException(
            $"{chunkName.ToString()}: invalid {reason} in precompiled chunk"
        );
}
