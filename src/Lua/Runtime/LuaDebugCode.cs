namespace Lua.Runtime;

internal interface ILuaDebugSession
{
    object Identity { get; }

    LuaDebugCode GetExecutionCode(LuaClosure closure);

    void ExecutionEnded();

    ValueTask<int> CheckpointAsync(
        LuaState state,
        LuaClosure closure,
        int pc,
        int line,
        bool lineEntry,
        CancellationToken cancellationToken
    );
}

internal class LuaDebugCode
{
    internal readonly object Identity;
    internal readonly Prototype Prototype;
    internal readonly Instruction[] Code;
    internal volatile bool Enabled;

    internal LuaDebugCode(object identity, Prototype prototype)
    {
        Identity = identity;
        Prototype = prototype;
        Code = prototype.Code.ToArray();
        var points = new bool[Code.Length];
        void Mark(int pc)
        {
            if ((uint)pc < (uint)points.Length)
                points[pc] = true;
        }
        Mark(0);
        for (var pc = 0; pc < Code.Length; pc++)
        {
            var instruction = Code[pc];
            if (
                pc != 0
                && !prototype.LineInfo.IsEmpty
                && prototype.LineInfo[pc] != prototype.LineInfo[pc - 1]
            )
                Mark(pc);
            switch (instruction.OpCode)
            {
                case OpCode.Jmp:
                case OpCode.ForLoop:
                case OpCode.TForLoop:
                    Mark(pc + 1 + instruction.SBx);
                    break;
                case OpCode.ForPrep:
                    var target = pc + 1 + instruction.SBx;
                    if (
                        target <= pc
                        || Code[target].OpCode != OpCode.ForLoop
                        || (
                            !prototype.LineInfo.IsEmpty
                            && prototype.LineInfo[target] != prototype.LineInfo[pc]
                        )
                    )
                        Mark(target);
                    break;
                case OpCode.Call:
                case OpCode.TailCall:
                case OpCode.TForCall:
                case OpCode.Return:
                    Mark(pc);
                    if (instruction.OpCode is OpCode.Call or OpCode.TForCall)
                        Mark(pc + 1);
                    break;
                case OpCode.Eq:
                case OpCode.Lt:
                case OpCode.Le:
                case OpCode.Test:
                case OpCode.TestSet:
                    Mark(pc + 2);
                    break;
                case OpCode.LoadBool when instruction.C != 0:
                    Mark(pc + 2);
                    break;
            }
        }
        for (var pc = 0; pc < Code.Length; pc++)
            if (points[pc] && Code[pc].OpCode != OpCode.ExtraArg)
                Code[pc] = (uint)LuaVirtualMachine.DebuggerBreakOpCode;
    }

    internal void ResumeAt(int pc)
    {
        Code[pc] = (uint)LuaVirtualMachine.DebuggerResumeOpCode;
    }

    internal Instruction RestoreAt(int pc)
    {
        Code[pc] = (uint)LuaVirtualMachine.DebuggerBreakOpCode;
        return Prototype.Code[pc];
    }
}
