namespace Lua.Runtime;

internal interface ILuaDebugSession
{
    object Identity { get; }

    LuaDebugCode GetExecutionCode(LuaClosure closure);

    void ExecutionEnded();

    void ThreadDisposed(LuaState state);

    ValueTask<int> CheckpointAsync(
        LuaState state,
        LuaClosure closure,
        int pc,
        int line,
        bool lineEntry,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// A private instruction copy for one prototype, used to place debugger checkpoints.
/// Checkpoints are only patched into the copy while controls need them; a disabled copy is
/// byte-for-byte identical to <see cref="Prototype"/>'s code, so an attached but idle debugger
/// does not change the dispatch of ordinary instructions.
/// </summary>
internal class LuaDebugCode
{
    internal readonly object Identity;
    internal readonly Prototype Prototype;
    internal readonly Instruction[] Code;
    readonly int[] traps;
    internal volatile bool Enabled;

    internal LuaDebugCode(object identity, Prototype prototype)
    {
        Identity = identity;
        Prototype = prototype;
        Code = prototype.Code.ToArray();
        traps = FindTraps(prototype);
    }

    internal void SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
            return;
        Enabled = enabled;
        if (enabled)
        {
            foreach (var pc in traps)
                Code[pc] = (uint)LuaVirtualMachine.DebuggerBreakOpCode;
            return;
        }

        var source = Prototype.Code;
        foreach (var pc in traps)
            Code[pc] = source[pc];
    }

    internal void ResumeAt(int pc)
    {
        Code[pc] = (uint)LuaVirtualMachine.DebuggerResumeOpCode;
    }

    internal Instruction RestoreAt(int pc)
    {
        // Re-arm this checkpoint for later visits, unless controls were released while stopped.
        Code[pc] = Enabled ? (uint)LuaVirtualMachine.DebuggerBreakOpCode : Prototype.Code[pc];
        return Prototype.Code[pc];
    }

    static int[] FindTraps(Prototype prototype)
    {
        var code = prototype.Code;
        var lines = prototype.LineInfo;
        var points = new bool[code.Length];
        void Mark(int pc)
        {
            if ((uint)pc < (uint)points.Length)
                points[pc] = true;
        }
        Mark(0);
        for (var pc = 0; pc < code.Length; pc++)
        {
            var instruction = code[pc];
            if (pc != 0 && !lines.IsEmpty && lines[pc] != lines[pc - 1])
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
                        || target >= code.Length
                        || code[target].OpCode != OpCode.ForLoop
                        || (!lines.IsEmpty && lines[target] != lines[pc])
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

        var count = 0;
        for (var pc = 0; pc < points.Length; pc++)
            if (IsTrapPoint(points, code, pc))
                count++;
        var traps = new int[count];
        var index = 0;
        for (var pc = 0; pc < points.Length; pc++)
            if (IsTrapPoint(points, code, pc))
                traps[index++] = pc;
        return traps;
    }

    static bool IsTrapPoint(bool[] points, ReadOnlySpan<Instruction> code, int pc) =>
        points[pc] && code[pc].OpCode != OpCode.ExtraArg;
}
