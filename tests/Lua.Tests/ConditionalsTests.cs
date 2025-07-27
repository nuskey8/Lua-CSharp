namespace Lua.Tests;

public class ConditionalsTests
{
    [Test]
    public async Task Test_Clamp()
    {
        var source = @"
function clamp(x, min, max)
    return x < min and min or (x > max and max or x)
end

return clamp(0, 1, 25), clamp(10, 1, 25), clamp(30, 1, 25)
";
        var result = await LuaGlobalState.Create().DoStringAsync(source);

        Assert.That(result, Has.Length.EqualTo(3));
        Assert.That(result[0], Is.EqualTo(new LuaValue(1)));
        Assert.That(result[1], Is.EqualTo(new LuaValue(10)));
        Assert.That(result[2], Is.EqualTo(new LuaValue(25)));
    }
}