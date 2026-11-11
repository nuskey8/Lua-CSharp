using Lua.Debugging;
using Lua.Runtime;
using NUnit.Framework;

namespace Lua.Debugger.Tests;

/// <summary>
/// The debugger trampoline occupies the two opcode encodings the six-bit instruction field
/// leaves unused. This guards the day someone grows the published set into them.
/// </summary>
public class DebuggerTrapTests
{
    [Test]
    public void TrapEncodingsStayOutsideThePublishedInstructionSet()
    {
        var highest = Enum.GetValues<OpCode>().Select(static op => (byte)op).Max();
        Assert.That(
            highest,
            Is.LessThan(62),
            "LuaVirtualMachine reserves opcode 62/63 for debugger traps; move the trampoline "
                + "or widen Instruction.SizeOp before adding opcodes here."
        );
        Assert.That(Instruction.SizeOp, Is.EqualTo(6));
    }

    [Test]
    public void TrapEncodingDoesNotOverflowTheInstructionNameTable()
    {
        // Diagnostics must not throw while a debugger checkpoint is on the stack.
        var trap = new Instruction(63);
        Assert.That(trap.ToString(), Is.EqualTo("TRAP 63"));
        Assert.That(trap.OpCode, Is.EqualTo((OpCode)63));
    }

    [Test]
    public void ResetRestoresOriginalInstructionsWhileStillAttached()
    {
        using var state = LuaState.Create();
        using var debugger = new LuaDebugger(state);
        debugger.Attach();
        var closure = state.Load("local value = 42\nreturn value", "=reset");
        debugger.RegisterSource(closure);
        debugger.SetBreakpoints("=reset", [2]);

        var code = ((ILuaDebugSession)debugger).GetExecutionCode(closure);
        Assert.That(code.Enabled, Is.True);

        debugger.Reset();
        Assert.That(debugger.IsAttached, Is.True);
        Assert.That(code.Code, Is.EqualTo(closure.Proto.Code.ToArray()));
    }
}
