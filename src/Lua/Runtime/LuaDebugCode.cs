namespace Lua.Runtime;

internal interface ILuaDebugSession
{
    object Identity { get; }

    LuaDebugCode GetExecutionCode(Prototype prototype);

    // The global state's active execution count just reached zero.
    void ExecutionQuiesced();

    void ThreadDisposed(LuaState state);

    ValueTask<int> CheckpointAsync(
        LuaState state,
        LuaDebugCode code,
        int pc,
        int line,
        bool isLineEntry,
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
