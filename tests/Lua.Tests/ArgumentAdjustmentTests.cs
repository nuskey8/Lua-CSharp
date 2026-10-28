using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class ArgumentAdjustmentTests(LuaStringMode mode)
{
    LuaState CreateState(bool hooks)
    {
        var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        if (hooks)
            state.SetHook(new LuaFunction((context, _) => new(context.Return())), "cr");
        return state;
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task TailCallsFillMissingParameters(bool varargs, bool hooks)
    {
        using var state = CreateState(hooks);
        var parameters = varargs ? "a,b,..." : "a,b";
        foreach (var result in new[] { "a", "b" })
        {
            var values = await state.DoStringAsync(
                "local function f("
                    + parameters
                    + ") return "
                    + result
                    + " end; local stale={}; return f()"
            );
            Assert.That(values, Is.EqualTo(new LuaValue[] { LuaValue.Nil }));
        }
        var partial = await state.DoStringAsync(
            "local function f(" + parameters + ") return a,b end; local stale={}; return f(7)"
        );
        Assert.That(partial, Is.EqualTo(new LuaValue[] { 7, LuaValue.Nil }));
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task RunAsyncFillsMissingParametersAndPreservesPrefix(bool varargs, bool hooks)
    {
        using var state = CreateState(hooks);
        var parameters = varargs ? "a,b,..." : "a,b";
        var function = (
            await state.DoStringAsync("return function(" + parameters + ") return b end")
        )[0]
            .Read<LuaFunction>();
        state.Push(42);
        var count = await state.RunAsync(function);
        using (var results = state.ReadStack(count))
            Assert.That(results.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { LuaValue.Nil }));
        Assert.That(state.Pop(), Is.EqualTo(new LuaValue(42)));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [Test]
    public async Task NativeCallbacksKeepTheirActualArgumentCount()
    {
        using var state = CreateState(false);
        state.Environment["native"] = new LuaFunction(
            (context, _) => new(context.Return(context.ArgumentCount))
        );
        var result = await state.DoStringAsync("return native(),native(1,nil,3)");
        Assert.That(result, Is.EqualTo(new LuaValue[] { 0, 3 }));
    }
}
