namespace Lua.Tests;

public class LoopTests
{
    [Test]
    public async Task Test_NumericFor()
    {
        var source = @"
local n = 0
for i = 1, 10 do
    n = n + i
end
return n";
        var result = await LuaGlobalState.Create().DoStringAsync(source);

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(55)));
    }

    [Test]
    public async Task Test_NumericFor_WithStep()
    {
        var source = @"
local n = 0
for i = 0, 10, 2 do
    n = n + i
end
return n";
        var result = await LuaGlobalState.Create().DoStringAsync(source);

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(30)));
    }

    [Test]
    public async Task Test_While()
    {
        var source = @"
local n = 0
while n < 100 do
    n = n + 1
end
return n";
        var result = await LuaGlobalState.Create().DoStringAsync(source);

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(100)));
    }
}