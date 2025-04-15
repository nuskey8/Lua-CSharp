using Lua.CodeAnalysis.Syntax;
using Lua.CodeAnalysis.Syntax.Nodes;

namespace Lua.Tests;

public class StringTests
{
    [TestCase("\r")]
    [TestCase("\n")]
    [TestCase("\r\n")]
    public async Task Test_ShortString_RealNewLine(string newLine)
    {
        var result = await LuaState.Create().DoStringAsync($"return \"\\{newLine}\"");
        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue("\n")));
    }
}