using Lua.Runtime;

namespace Lua.Tests;

public class InstructionTests
{
    [Test]
    public void Test()
    {
        var instruction = new Instruction();

        instruction.OpCode = OpCode.LoadK;
        Assert.That(instruction.OpCode, Is.EqualTo(OpCode.LoadK));

        instruction.A = 1;
        instruction.B = 2;
        instruction.C = 3;
        Assert.Multiple(() =>
        {
            Assert.That(instruction.A, Is.EqualTo(1));
            Assert.That(instruction.B, Is.EqualTo(2));
            Assert.That(instruction.C, Is.EqualTo(3));
            Assert.That(instruction.OpCode, Is.EqualTo(OpCode.LoadK));
        });

        instruction.Bx = 4;
        Assert.Multiple(() =>
        {
            Assert.That(instruction.Bx, Is.EqualTo(4));
            Assert.That(instruction.OpCode, Is.EqualTo(OpCode.LoadK));
        });

        instruction.SBx = -4;
        Assert.Multiple(() =>
        {
            Assert.That(instruction.SBx, Is.EqualTo(-4));
            Assert.That(instruction.OpCode, Is.EqualTo(OpCode.LoadK));
        });

        instruction.Ax = 5;
        Assert.Multiple(() =>
        {
            Assert.That(instruction.Ax, Is.EqualTo(5));
            Assert.That(instruction.OpCode, Is.EqualTo(OpCode.LoadK));
        });
    }
}
