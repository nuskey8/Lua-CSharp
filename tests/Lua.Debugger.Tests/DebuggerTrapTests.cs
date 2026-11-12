using Lua.Debugging;
using Lua.Runtime;
using NUnit.Framework;

namespace Lua.Debugger.Tests;

/// <summary>
/// The debugger checkpoint reserves the highest encoding in the six-bit opcode field.
/// </summary>
public class DebuggerTrapTests
{
    [Test]
    public void TrapEncodingStaysOutsideThePublishedInstructionSet()
    {
        var highest = Enum.GetValues<OpCode>().Select(static op => (byte)op).Max();
        Assert.That(highest, Is.LessThan((byte)LuaVirtualMachine.DebuggerBreakOpCode));
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

        var code = ((ILuaDebugSession)debugger).GetExecutionCode(closure.Proto);
        Assert.That(code.Enabled, Is.True);

        debugger.Reset();
        Assert.That(debugger.IsAttached, Is.True);
        Assert.That(code.Code, Is.EqualTo(closure.Proto.Code.ToArray()));
    }
}
