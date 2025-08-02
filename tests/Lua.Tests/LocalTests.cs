namespace Lua.Tests;

public class LocalTests
{
    [Test]
    public async Task Test_LocalFunction_Nil_1()
    {
        var source = @"
local function f(x) x = nil; return x end
return f(10)";
        var result = await LuaState.Create().DoStringAsync(source);

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(LuaValue.Nil));
    }

    [Test]
    public async Task Test_LocalFunction_Nil_2()
    {
        var source = @"
local function f() local x; return x end
return f(10)";
        var result = await LuaState.Create().DoStringAsync(source);

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(LuaValue.Nil));
    }

    [Test]
    public async Task Test_LocalFunction_Nil_3()
    {
        var source = @"
local function f(x) x = nil; local y; return x, y end
return f(10)";
        var result = await LuaState.Create().DoStringAsync(source);

        Assert.That(result, Has.Length.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(result[0], Is.EqualTo(LuaValue.Nil));
            Assert.That(result[1], Is.EqualTo(LuaValue.Nil));
        });
    }

    [Test]
    public async Task Test_LocalVariable_1()
    {
        var source = "local i = 10; do local i = 100; return i end";
        var result = await LuaState.Create().DoStringAsync(source);

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(100)));
    }

    [Test]
    public async Task Test_LocalVariable_2()
    {
        var source = @"
local i = 10
do local i = 100 end
return i";
        var result = await LuaState.Create().DoStringAsync(source);

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(10)));
    }
}