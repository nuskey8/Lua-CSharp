using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class StandardLibraryCallbackTests(LuaStringMode mode)
{
    LuaState CreateState()
    {
        var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        return state;
    }

    static readonly string[] CallbackOperations =
    [
        "pairs(setmetatable({},{__pairs=callback}))",
        "ipairs(setmetatable({},{__ipairs=callback}))",
        "tostring(setmetatable({},{__tostring=callback}))",
        "string.format('%s',setmetatable({},{__tostring=callback}))",
        "string.gsub('a','.',callback)",
        "string.gsub('a','.',setmetatable({},{__index=callback}))",
        "table.sort({2,1},callback)",
        "local meta={__lt=callback};table.sort({setmetatable({},meta),setmetatable({},meta)})",
        "table.concat(setmetatable({},{__len=callback}))",
        "table.insert(setmetatable({},{__len=callback}),42)",
        "table.remove(setmetatable({},{__len=callback}))",
        "table.sort(setmetatable({},{__len=callback}))",
        "table.unpack(setmetatable({},{__len=callback}))",
    ];

    [TestCaseSource(nameof(CallbackOperations))]
    public async Task CallbacksCannotYieldAndRestoreYieldability(string operation)
    {
        using var state = CreateState();
        state.Environment["operation"] = state.Load(operation, "@callback");
        await state.DoStringAsync(
            """
            callback=function() coroutine.yield('unexpected') end
            local co=coroutine.create(function()
                local ok,e=pcall(operation)
                assert(not ok and e:find('attempt to yield across a C-call boundary',1,true))
                coroutine.yield('after callback')
                return 42
            end)
            local ok,value=coroutine.resume(co)
            assert(ok and value=='after callback')
            ok,value=coroutine.resume(co)
            assert(ok and value==42 and coroutine.status(co)=='dead')
            """
        );
        Assert.That(state.NonYieldableCallCount, Is.Zero);
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [Test, Combinatorial]
    public async Task HostCallbacksCanAwaitAndRestoreState(
        [ValueSource(nameof(CallbackOperations))] string operation,
        [Values(false, true)] bool cancellation
    )
    {
        using var state = CreateState();
        using var source = new CancellationTokenSource();
        var suspension = new ManualSuspension();
        var calls = 0;
        state.Environment["callback"] = new LuaFunction(
            async (context, token) =>
            {
                calls++;
                await suspension;
                token.ThrowIfCancellationRequested();
                return context.Return(0);
            }
        );
        var task = state.DoStringAsync(operation, cancellationToken: source.Token).AsTask();
        Assert.That(task.IsCompleted, Is.False);
        if (cancellation)
            source.Cancel();
        suspension.Resume();
        if (cancellation)
            Assert.ThrowsAsync<LuaCanceledException>(async () => await task);
        else
            await task;
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(state.NonYieldableCallCount, Is.Zero);
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }
}
