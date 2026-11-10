using Lua.Standard;

namespace Lua.Tests;

/// <summary>
/// Lua 5.2 (and 5.3/5.4) reject a <c>next</c> cursor that is not part of the table, while a field
/// cleared during traversal stays a valid cursor. All expectations here were taken from a native
/// Lua 5.2 build.
/// </summary>
public class NextIterationTests
{
    static LuaState CreateState()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        return state;
    }

    [Test]
    public async Task RejectsAKeyThatIsNotInTheTable()
    {
        using var state = CreateState();
        var results = await state.DoStringAsync(
            "local t = {a = 1} return pcall(next, t, 'missing')",
            "=next"
        );
        Assert.That(results[0].Read<bool>(), Is.False);
        Assert.That(results[1].ToString(), Does.Contain("invalid key to 'next'"));
    }

    [Test]
    public async Task RejectsAnIntegerKeyAboveTheArrayPart()
    {
        using var state = CreateState();
        var results = await state.DoStringAsync("return pcall(next, {}, 1)", "=next");
        Assert.That(results[0].Read<bool>(), Is.False);
        Assert.That(results[1].ToString(), Does.Contain("invalid key to 'next'"));
    }

    [Test]
    public async Task ClearedLastKeyStillEndsTheIteration()
    {
        using var state = CreateState();
        var results = await state.DoStringAsync(
            "local t = {a = 1}\n"
                + "local k = next(t)\n"
                + "t[k] = nil\n"
                + "local ok, v = pcall(next, t, k)\n"
                + "return ok, v == nil",
            "=next"
        );
        Assert.That(results[0].Read<bool>(), Is.True);
        Assert.That(results[1].Read<bool>(), Is.True);
    }

    [Test]
    public async Task ClearedKeyContinuesToTheNextEntry()
    {
        using var state = CreateState();
        var results = await state.DoStringAsync(
            "local t = {a = 1, b = 2, c = 3}\n"
                + "local k = next(t)\n"
                + "t[k] = nil\n"
                + "local ok, n = pcall(next, t, k)\n"
                + "return ok, n ~= nil",
            "=next"
        );
        Assert.That(results[0].Read<bool>(), Is.True);
        Assert.That(results[1].Read<bool>(), Is.True);
    }

    [Test]
    public async Task ClearingEveryKeyDuringPairsCompletes()
    {
        using var state = CreateState();
        var results = await state.DoStringAsync(
            "local t = {a = 1, b = 2, c = 3}\n"
                + "local n = 0\n"
                + "for k in pairs(t) do t[k] = nil n = n + 1 end\n"
                + "return n, next(t) == nil",
            "=next"
        );
        Assert.That(results[0].Read<double>(), Is.EqualTo(3));
        Assert.That(results[1].Read<bool>(), Is.True);
    }

    [Test]
    public async Task InsertingAfterClearingInvalidatesTheCursor()
    {
        using var state = CreateState();
        var results = await state.DoStringAsync(
            "local t = {a = 1}\n"
                + "local k = next(t)\n"
                + "t[k] = nil\n"
                + "t.b = 2\n"
                + "return pcall(next, t, k)",
            "=next"
        );
        Assert.That(results[0].Read<bool>(), Is.False);
        Assert.That(results[1].ToString(), Does.Contain("invalid key to 'next'"));
    }

    [Test]
    public async Task NilKeyAndArrayHolesStayValid()
    {
        using var state = CreateState();
        var results = await state.DoStringAsync(
            "local ok = pcall(next, {}, nil)\n"
                + "local t = {1, 2, 3}\n"
                + "t[2] = nil\n"
                + "local k, v = next(t, 1)\n"
                + "return ok, k, v",
            "=next"
        );
        Assert.That(results[0].Read<bool>(), Is.True);
        Assert.That(results[1].Read<double>(), Is.EqualTo(3));
        Assert.That(results[2].Read<double>(), Is.EqualTo(3));
    }
}
