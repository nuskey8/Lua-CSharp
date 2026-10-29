using Lua.Runtime;

namespace Lua.Tests;

public class InstructionTests
{
    [TestCase(0)]
    [TestCase(17)]
    [TestCase(Instruction.MaxArgBx)]
    public void ClosureFactoryEncodesUnsignedPrototypeIndex(int prototypeIndex)
    {
        var instruction = Instruction.Closure(3, prototypeIndex);
        Assert.That(
            instruction.Value,
            Is.EqualTo(Instruction.CreateABx(OpCode.Closure, 3, prototypeIndex))
        );
        Assert.That(instruction.Bx, Is.EqualTo(prototypeIndex));
    }

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
