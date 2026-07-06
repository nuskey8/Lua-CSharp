using Lua.Standard;

namespace Lua.Tests;

public class LoopTests
{
    [Test]
    public async Task Test_NumericFor()
    {
        var source =
            @"
local n = 0
for i = 1, 10 do
    n = n + i
end
return n";
        var result = await LuaState.Create().DoStringAsync(source);

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(55)));
    }

    [Test]
    public async Task Test_NumericFor_WithStep()
    {
        var source =
            @"
local n = 0
for i = 0, 10, 2 do
    n = n + i
end
return n";
        var result = await LuaState.Create().DoStringAsync(source);

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(30)));
    }

    [Test]
    public async Task Test_While()
    {
        var source =
            @"
local n = 0
while n < 100 do
    n = n + 1
end
return n";
        var result = await LuaState.Create().DoStringAsync(source);

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(100)));
    }

    // Regression: returning from a metamethod truncated the stack to the result
    // register. When that register sat below a numeric for-loop's hidden control
    // slots (index/limit/step) — which happens when the assignment target is a
    // local declared before the loop — the control slots were clobbered, the step
    // decayed to 0, and the loop spun forever. A CancellationToken guards the run so
    // a re-regression fails fast instead of hanging the whole test suite.
    [Test]
    public async Task Test_NumericFor_MetamethodResultDoesNotCorruptLoopState()
    {
        var source =
            @"
local mt = {}
mt.__add = function(a, b) return setmetatable({ v = a.v + b.v }, mt) end
local acc = setmetatable({ v = 0 }, mt)
for i = 1, 5 do
    acc = acc + setmetatable({ v = i }, mt)
end
return acc.v";

        var state = LuaState.Create();
        state.OpenStandardLibraries();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await state.DoStringAsync(source, "@test.lua", cts.Token);

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(15)));
    }

    // Same regression, exercised through an __index metamethod (single-result return
    // path) with the target local living below the loop's control slots.
    [Test]
    public async Task Test_NumericFor_IndexMetamethodResultDoesNotCorruptLoopState()
    {
        var source =
            @"
local proxy = setmetatable({}, { __index = function(_, k) return k end })
local sum = 0
for i = 1, 5 do
    sum = sum + proxy[i]
end
return sum";

        var state = LuaState.Create();
        state.OpenStandardLibraries();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await state.DoStringAsync(source, "@test.lua", cts.Token);

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(15)));
    }
}
