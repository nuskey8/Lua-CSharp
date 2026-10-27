using System.Text;
using Lua.CodeAnalysis.Syntax;
using Lua.CodeAnalysis.Syntax.Nodes;
using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class CompilerReviewTests(LuaStringMode stringMode)
{
    LuaState CreateState()
    {
        var state = LuaState.Create(
            stringMode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        return state;
    }

    [TestCase("return 0x1+2", 3)]
    [TestCase("return 0x1-2", -1)]
    [TestCase("return 0x3*2", 6)]
    [TestCase("return 0x8/2", 4)]
    [TestCase("return 0x5%2", 1)]
    [TestCase("return 0x2^3", 8)]
    [TestCase("return 0x1;", 1)]
    [TestCase("return ({[0x1]=42})[0x1]", 42)]
    [TestCase("return 0x1--comment", 1)]
    public async Task HexLiteralsEndAtPunctuation(string source, double expected)
    {
        using var state = CreateState();
        var closure = state.Load(source, "hex-punctuation");
        foreach (
            var executable in new[] { closure, state.Load(closure.Proto.ToBytecode(), "roundtrip") }
        )
            Assert.That(
                (await state.ExecuteAsync(executable))[0].Read<double>(),
                Is.EqualTo(expected)
            );
    }

    [TestCase("0x0.8p-1073", double.Epsilon)]
    [TestCase("0x8p-1077", double.Epsilon)]
    [TestCase("0x8p-1078", 0)]
    [TestCase("0x18p-1078", 2 * double.Epsilon)]
    [TestCase("0x28p-1078", 2 * double.Epsilon)]
    [TestCase("0x0p2000", 0)]
    [TestCase("0x0p-2000", 0)]
    [TestCase("0xffffffffffffffffp0", 18446744073709551616d)]
    [TestCase("0xc8eb5bb682575ec", 9.048603916588457e17)]
    [TestCase("0x1p4294967296", 1)]
    public async Task HexNumbersAgreeAcrossCompilerSyntaxAndConversion(
        string literal,
        double expected
    )
    {
        using var state = CreateState();
        var results = await state.DoStringAsync($"return {literal},tonumber('{literal}')");
        Assert.That(results[0].Read<double>(), Is.EqualTo(expected));
        Assert.That(results[1].Read<double>(), Is.EqualTo(expected));
        var tree = LuaSyntaxTree.Parse("return " + literal);
        var value = ((NumericLiteralNode)((ReturnStatementNode)tree.Nodes[0]).Nodes[0]).Value;
        Assert.That(value, Is.EqualTo(expected));
    }

    [TestCase("0x1.g")]
    [TestCase("0x1z.2")]
    [TestCase("0x1p")]
    [TestCase("0x.p0")]
    [TestCase("0x1p0.5")]
    [TestCase("0x1p 2")]
    public async Task InvalidHexConversionReturnsNil(string text)
    {
        using var state = CreateState();
        var values = await state.DoStringAsync($"return tonumber('{text}')");
        Assert.That(values[0], Is.EqualTo(LuaValue.Nil));
    }

    [TestCase(248, true)]
    [TestCase(249, false)]
    [TestCase(300, false)]
    public async Task RegisterLimitMatchesLua52(int arguments, bool valid)
    {
        using var state = CreateState();
        var source =
            "return (function(...) return select('#',...) end)("
            + string.Join(",", Enumerable.Repeat("1", arguments))
            + ")";
        if (valid)
            Assert.That(
                (await state.DoStringAsync(source))[0].Read<double>(),
                Is.EqualTo(arguments)
            );
        else
            Assert.Throws<LuaCompileException>(() => state.Load(source, "register-limit"));
    }

    [Test]
    public async Task SuccessfulAndFailedCompilesCanReuseCompilerState()
    {
        using var state = CreateState();
        for (var i = 0; i < 20; i++)
        {
            Assert.Throws<LuaCompileException>(() =>
                state.Load("local function broken() local x=1; goto absent end", "failure")
            );
            var result = await state.DoStringAsync(
                $"local x={i}; local function f() return x end; return f()"
            );
            Assert.That(result[0].Read<double>(), Is.EqualTo(i));
        }
    }

    [Test]
    public async Task SyntaxDepthFailureRestoresCounter()
    {
        using var state = CreateState();
        var source = "return " + string.Concat(Enumerable.Repeat("not ", 300)) + "true";
        for (var i = 0; i < 5; i++)
        {
            Assert.Throws<LuaCompileException>(() => state.Load(source, "too-deep"));
            Assert.That(state.CallCount, Is.Zero);
        }
        Assert.That((await state.DoStringAsync("return 42"))[0].Read<double>(), Is.EqualTo(42));
    }
}

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
