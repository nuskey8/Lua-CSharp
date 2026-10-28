using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

public class CoroutineLifetimeTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task FailedCoroutineReleasesStackAndPreservesError(bool wrap, bool yieldFirst)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["wrap"] = wrap;
        state.Environment["yieldFirst"] = yieldFirst;
        var results = await state.DoStringAsync(
            """
            local thread
            local failure={}
            local read
            local function body()
                thread=coroutine.running()
                local captured=42
                read=function() return captured end
                if yieldFirst then coroutine.yield() end
                error(failure)
            end
            local run
            if wrap then
                run=coroutine.wrap(body)
            else
                thread=coroutine.create(body)
                run=function() return coroutine.resume(thread) end
            end
            local function resume()
                if wrap then return pcall(run) end
                return run()
            end
            if yieldFirst then resume() end
            local ok,err=resume()
            local again,why=resume()
            return thread,ok,err==failure,coroutine.status(thread),again,why,read(),debug.traceback(thread)
            """,
            "coroutine-lifetime"
        );
        var thread = results[0].Read<LuaState>();
        Assert.That(results[1], Is.EqualTo(new LuaValue(false)));
        Assert.That(results[2], Is.EqualTo(new LuaValue(true)));
        Assert.That(results[3].Read<string>(), Is.EqualTo("dead"));
        Assert.That(results[4], Is.EqualTo(new LuaValue(false)));
        Assert.That(results[5].Read<string>(), Does.Contain("cannot resume dead coroutine"));
        Assert.That(results[6], Is.EqualTo(new LuaValue(42)));
        Assert.That(results[7].Read<string>(), Does.Contain("coroutine-lifetime"));
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead));
        Assert.That(thread.GetStackValues().Length, Is.Zero);
        Assert.That(thread.CallStackFrameCount, Is.Zero);
        Assert.That((await state.DoStringAsync("return 42"))[0], Is.EqualTo(new LuaValue(42)));
    }
}
