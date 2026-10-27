using Lua.Standard;

namespace Lua.Tests;

public class RuntimeMetamethodFailureTests
{
    [TestCase("__add", "local a,b=value,value; local r=a+b; return r", false)]
    [TestCase("__add", "local a,b=value,value; local r=a+b; return r", true)]
    [TestCase("__add", "local a,b=value,value; a=a+b; return a", false)]
    [TestCase("__add", "local a,b=value,value; a=a+b; return a", true)]
    [TestCase("__unm", "local a=value; local r=-a; return r", false)]
    [TestCase("__unm", "local a=value; local r=-a; return r", true)]
    [TestCase("__unm", "local a=value; a=-a; return a", false)]
    [TestCase("__unm", "local a=value; a=-a; return a", true)]
    public async Task FaultedNativeMetamethodAlwaysPropagates(
        string name,
        string source,
        bool suspend
    )
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var metatable = new LuaTable();
        metatable[name] = new LuaFunction(
            async (context, token) =>
            {
                if (suspend)
                    await Task.Yield();
                throw new LuaRuntimeException(context.State, "metamethod-fault");
            }
        );
        state.Environment["value"] = new LuaTable { Metatable = metatable };
        var values = await state.DoStringAsync("return pcall(function() " + source + " end)");
        Assert.That(values[0].Read<bool>(), Is.False);
        Assert.That(values[1].Read<string>(), Does.Contain("metamethod-fault"));
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
        Assert.That((await state.DoStringAsync("return 42"))[0].Read<double>(), Is.EqualTo(42));
    }
}
