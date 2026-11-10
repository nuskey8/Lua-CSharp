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
}
