namespace Lua.Tests;

public class OperatorTests
{
    [TestCase(1, 6)]
    [TestCase(2, 7)]
    [TestCase(3, 8)]
    [TestCase(4, 9)]
    [TestCase(5, 10)]
    public async Task Test_Add(double a, double b)
    {
        var result = await LuaGlobalState.Create().DoStringAsync($"return {a} + {b}");
        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(a + b)));
    }

    [TestCase(1, 6)]
    [TestCase(2, 7)]
    [TestCase(3, 8)]
    [TestCase(4, 9)]
    [TestCase(5, 10)]
    public async Task Test_Sub(double a, double b)
    {
        var result = await LuaGlobalState.Create().DoStringAsync($"return {a} - {b}");
        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(a - b)));
    }

    [TestCase(1, 6)]
    [TestCase(2, 7)]
    [TestCase(3, 8)]
    [TestCase(4, 9)]
    [TestCase(5, 10)]
    public async Task Test_Mul(double a, double b)
    {
        var result = await LuaGlobalState.Create().DoStringAsync($"return {a} * {b}");
        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(a * b)));
    }

    [TestCase(1, 6)]
    [TestCase(2, 7)]
    [TestCase(3, 8)]
    [TestCase(4, 9)]
    [TestCase(5, 10)]
    public async Task Test_Div(double a, double b)
    {
        var result = await LuaGlobalState.Create().DoStringAsync($"return {a} / {b}");
        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(a / b)));
    }

    [TestCase(1, 6)]
    [TestCase(2, 7)]
    [TestCase(3, 8)]
    [TestCase(4, 9)]
    [TestCase(5, 10)]
    public async Task Test_Mod(double a, double b)
    {
        var result = await LuaGlobalState.Create().DoStringAsync($"return {a} % {b}");
        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(a % b)));
    }

    [TestCase(1, 6)]
    [TestCase(2, 7)]
    [TestCase(3, 8)]
    [TestCase(4, 9)]
    [TestCase(5, 10)]
    public async Task Test_Pow(double a, double b)
    {
        var result = await LuaGlobalState.Create().DoStringAsync($"return {a} ^ {b}");
        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(Math.Pow(a, b))));
    }

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public async Task Test_Or(bool a, bool b)
    {
        string strA = a.ToString().ToLower(), strB = b.ToString().ToLower();
        var result = await LuaGlobalState.Create().DoStringAsync($"return {strA} or {strB}");
        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(a || b)));
    }

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public async Task Test_And(bool a, bool b)
    {
        string strA = a.ToString().ToLower(), strB = b.ToString().ToLower();
        var result = await LuaGlobalState.Create().DoStringAsync($"return {strA} and {strB}");
        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(a && b)));
    }

    [TestCase(1, 6)]
    [TestCase(9, 2)]
    [TestCase(5, 5)]
    public async Task Test_LessThan(double a, double b)
    {
        var result = await LuaGlobalState.Create().DoStringAsync($"return {a} < {b}");
        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(a < b)));
    }

    [TestCase(1, 6)]
    [TestCase(9, 2)]
    [TestCase(5, 5)]
    public async Task Test_LessThanOrEquals(double a, double b)
    {
        var result = await LuaGlobalState.Create().DoStringAsync($"return {a} <= {b}");
        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(a <= b)));
    }

    [TestCase(1, 6)]
    [TestCase(9, 2)]
    [TestCase(5, 5)]
    public async Task Test_GreaterThan(double a, double b)
    {
        var result = await LuaGlobalState.Create().DoStringAsync($"return {a} > {b}");
        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(a > b)));
    }

    [TestCase(1, 6)]
    [TestCase(9, 2)]
    [TestCase(5, 5)]
    public async Task Test_GreaterThanOrEquals(double a, double b)
    {
        var result = await LuaGlobalState.Create().DoStringAsync($"return {a} >= {b}");
        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(a >= b)));
    }
}