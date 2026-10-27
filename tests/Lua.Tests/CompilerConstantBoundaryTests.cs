using System.Text;
using Lua.CodeAnalysis.Syntax;
using Lua.CodeAnalysis.Syntax.Nodes;
using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

public class CompilerConstantBoundaryTests
{
    [Test]
    public async Task ConstantBeyondBxUsesLoadKxAndRoundTrips()
    {
        using var state = LuaState.Create();
        var source = new StringBuilder("local x\n");
        for (var i = 0; i <= Instruction.MaxArgBx + 1; i++)
            source.Append("x=").Append(i).Append('\n');
        source.Append("return x");
        var closure = state.Load(source.ToString(), "large-constants");
        Assert.That(closure.Proto.Code.ToArray().Any(i => i.OpCode == OpCode.LoadKX), Is.True);
        Assert.That(
            (await state.ExecuteAsync(closure))[0].Read<double>(),
            Is.EqualTo(Instruction.MaxArgBx + 1)
        );
        var restored = state.Load(closure.Proto.ToBytecode(), "large-constants-roundtrip");
        Assert.That(
            (await state.ExecuteAsync(restored))[0].Read<double>(),
            Is.EqualTo(Instruction.MaxArgBx + 1)
        );
    }
}
