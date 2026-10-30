using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class ProtectedCallTests(LuaStringMode stringMode)
{
    LuaState CreateState()
    {
        var state = LuaState.Create(
            stringMode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        return state;
    }

    [TestCase("return 'handled',99", "handled")]
    [TestCase("return", null)]
    [TestCase("error('handler failed',0)", "error in error handling")]
    public async Task XpcallReturnsExactlyOneHandlerResult(string handler, string? expected)
    {
        using var state = CreateState();
        var result = await state.DoStringAsync(
            "return xpcall(function() error('fail',0) end,function(e) " + handler + " end)"
        );
        Assert.That(
            result,
            Is.EqualTo(new LuaValue[] { false, expected == null ? LuaValue.Nil : expected })
        );
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
        Assert.That(state.ErrorHandlerIndex, Is.Zero);
    }

    [Test]
    public async Task HandlerIsOnlyValidatedWhenAnErrorOccurs()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            local ok,a,b=xpcall(function(a,b) return a,b end,nil,12,34)
            assert(ok and a==12 and b==34)
            local ok,e=xpcall(function() error('failure') end,nil)
            assert(not ok and e=='error in error handling')
            local handler=setmetatable({}, {__call=function(self,e) return e end})
            local ok,e=xpcall(function() error(42) end,handler)
            assert(not ok and e==42)
            """
        );
    }

    [TestCase(0, false)]
    [TestCase(0, true)]
    [TestCase(2, false)]
    [TestCase(2, true)]
    [TestCase(8, false)]
    [TestCase(8, true)]
    public async Task XpcallSupportsOverlappingAndForwardReturnBuffers(int returnBase, bool failure)
    {
        using var state = CreateState();
        state.Stack.Push(
            new LuaFunction(
                (context, _) =>
                {
                    if (failure)
                        throw new LuaRuntimeException(context.State, "failure");
                    return new(context.Return(context.GetArgument(0), 43));
                }
            )
        );
        state.Stack.Push(new LuaFunction((context, _) => new(context.Return("handled"))));
        state.Stack.Push(42);
        var function = state.Environment["xpcall"].Read<LuaFunction>();
        var count = await state.RunAsync(function, 3, returnBase);
        Assert.That(count, Is.EqualTo(failure ? 2 : 3));
        Assert.That(
            state.Stack.GetBuffer().Slice(returnBase, count).ToArray(),
            Is.EqualTo(
                failure ? new LuaValue[] { false, "handled" } : new LuaValue[] { true, 42, 43 }
            )
        );
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task HandlerCanInspectFailingFramesAndLocals(bool asynchronous)
    {
        using var state = CreateState();
        var suspension = new ManualSuspension();
        state.Environment["pause"] = new LuaFunction(
            async (context, _) =>
            {
                if (asynchronous)
                    await suspension;
                return context.Return();
            }
        );
        var operation = state
            .DoStringAsync(
                """
                local escaped
                local function namedFailure()
                    local value=41
                    escaped=function() return value end
                    pause()
                    error('fail')
                end
                local ok,message=xpcall(function() namedFailure() end,function(e)
                    local trace=debug.traceback(e)
                    assert(trace:find('namedFailure',1,true))
                    for level=1,10 do
                        local info=debug.getinfo(level,'f')
                        if info and info.func==namedFailure then
                            for index=1,10 do
                                local name,value=debug.getlocal(level,index)
                                if name=='value' then
                                    assert(value==41)
                                    debug.setlocal(level,index,42)
                                    return 'handled'
                                end
                            end
                        end
                    end
                    error('missing failing local')
                end)
                return ok,message,escaped()
                """,
                "@protected.lua"
            )
            .AsTask();
        if (asynchronous)
            suspension.Resume();
        Assert.That(await operation, Is.EqualTo(new LuaValue[] { false, "handled", 42 }));
        Assert.That(state.CallStackFrameCount, Is.Zero);
        Assert.That(state.OpenUpValues.Length, Is.Zero);
    }

    [Test]
    public async Task NestedProtectedCallsRestoreTheOuterHandler()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            local hits=0
            local ok,e=xpcall(function()
                assert(not pcall(error,'caught'))
                local f,message=load(function() error('reader failed') end)
                assert(not f and message:find('reader failed',1,true))
                local ok,value=xpcall(function() error('inner',0) end,function(e) return e end)
                assert(not ok and value=='inner')
                error('outer',0)
            end,function(e) hits=hits+1;return e end)
            assert(not ok and e=='outer' and hits==1)
            """
        );
        Assert.That(state.ErrorHandlerIndex, Is.Zero);
    }

    [Test]
    public async Task HostErrorHandlerCanSuspendWithoutLosingTheErrorOrFrames()
    {
        using var state = CreateState();
        var suspension = new ManualSuspension();
        const string message = "host failure ế";
        state.Environment["fail"] = new LuaFunction((_, _) => throw new IOException(message));
        state.Environment["handler"] = new LuaFunction(
            async (context, _) =>
            {
                var error = context.GetArgument(0);
                Assert.That(context.State.CallStackFrameCount, Is.GreaterThan(2));
                await suspension;
                return context.Return(error, 99);
            }
        );
        var operation = state.DoStringAsync("return xpcall(fail,handler)").AsTask();
        suspension.Resume();
        Assert.That(
            await operation,
            Is.EqualTo(new LuaValue[] { false, state.Platform.EncodeText(message) })
        );
        Assert.That(state.ErrorHandlerIndex, Is.Zero);
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [Test]
    public async Task ErrorHandlerCannotYield()
    {
        using var state = CreateState();
        var result = await state.DoStringAsync(
            """
            local co=coroutine.create(function()
                return xpcall(function() error('fail') end,coroutine.yield)
            end)
            local ok,status,message=coroutine.resume(co)
            return ok,status,message,coroutine.status(co)
            """
        );
        Assert.That(
            result,
            Is.EqualTo(new LuaValue[] { true, false, "error in error handling", "dead" })
        );
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancellationStillEscapesXpcall(bool cancelInHandler)
    {
        using var state = CreateState();
        using var source = new CancellationTokenSource();
        state.Environment["cancel"] = new LuaFunction(
            (context, _) =>
            {
                source.Cancel();
                return new(context.Return());
            }
        );
        Assert.ThrowsAsync<LuaCanceledException>(async () =>
            await state.DoStringAsync(
                cancelInHandler
                    ? "return xpcall(function() error('fail') end,cancel)"
                    : "return xpcall(cancel,function() error('handler must not run') end)",
                cancellationToken: source.Token
            )
        );
        Assert.That(state.ErrorHandlerIndex, Is.Zero);
        Assert.That((await state.DoStringAsync("return 42"))[0], Is.EqualTo(new LuaValue(42)));
    }
}
