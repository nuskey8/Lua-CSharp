using Lua.Runtime;

namespace Lua.Tests;

public class LuaStackCapacityTests
{
    [Test]
    public void EmptyStackCanGrowAndRejectsOverflowingCapacity()
    {
        var stack = new LuaStack(0);
        stack.Push(42);
        Assert.That(stack.Pop().Read<double>(), Is.EqualTo(42));
        Assert.Throws<LuaStackOverflowException>(() => stack.EnsureCapacity(int.MaxValue));
        Assert.That(stack.Count, Is.Zero);
    }
}
