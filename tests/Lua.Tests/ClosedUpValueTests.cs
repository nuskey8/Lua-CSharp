using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class ClosedUpValueTests(LuaStringMode mode)
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task NewCapturesUseTheLocalAfterAnEarlierCaptureWasClosed(bool asynchronous)
    {
        using var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        state.Environment["close"] = new LuaFunction(
            async (context, _) =>
            {
                if (asynchronous)
                    await Task.Yield();
                context.GetArgument<LuaClosure>(0).UpValues[0].Close();
                return context.Return();
            }
        );
        var result = await state.DoStringAsync(
            """
            local n=1
            local old=function() return n end
            close(old)
            n=2
            local newer=function() return n end
            local set=function(value) n=value end
            local before=newer()
            set(3)
            return old(),before,newer(),debug.upvalueid(old,1)==debug.upvalueid(newer,1),
                debug.upvalueid(newer,1)==debug.upvalueid(set,1)
            """
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { 1, 2, 3, false, true }));
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }
}
