using System.Runtime.CompilerServices;
using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class TailCallLifetimeTests(LuaStringMode mode)
{
    LuaState CreateState(bool hooks = false)
    {
        var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        if (hooks)
            state.SetHook(new LuaFunction((context, _) => new(context.Return())), "cr");
        state.Stack.PushRange([101, 102]);
        return state;
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task VarargTailCallsUseConstantStackSpace(bool hooks, bool callable)
    {
        using var state = CreateState(hooks);
        var stackSizes = new HashSet<int>();
        var calls = 0;
        state.Environment["observe"] = new LuaFunction(
            (context, _) =>
            {
                calls++;
                stackSizes.Add(context.State.Stack.Count);
                return new(context.Return());
            }
        );
        var source = callable
            ? """
                local step
                step=setmetatable({}, {__call=function(self,n,...)
                    if n==0 then return ... end
                    observe()
                    return step(n-1,...)
                end})
                return step(256,7,nil,35)
                """
            : """
                local function step(n,...)
                    if n==0 then return ... end
                    observe()
                    return step(n-1,...)
                end
                return step(256,7,nil,35)
                """;
        var result = await state.DoStringAsync(source);
        Assert.That(result, Is.EqualTo(new LuaValue[] { 7, LuaValue.Nil, 35 }));
        Assert.That(calls, Is.EqualTo(256));
        Assert.That(stackSizes.Count, Is.EqualTo(1));
        Assert.That(state.GetStackValues().ToArray(), Is.EqualTo(new LuaValue[] { 101, 102 }));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TailCallsDiscardOldVarargsBeforeFillingMissingParameters(bool hooks)
    {
        using var state = CreateState(hooks);
        var result = await state.DoStringAsync(
            """
            local function result(a,b,c,...) return a,b,c,select('#',...),... end
            local function forward(...) return result(42) end
            return forward(7,nil,35)
            """
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { 42, LuaValue.Nil, LuaValue.Nil, 0 }));
        Assert.That(state.GetStackValues().ToArray(), Is.EqualTo(new LuaValue[] { 101, 102 }));
    }

    [TestCase("arguments")]
    [TestCase("closure")]
    [TestCase("completed-closure")]
    public async Task DiscardedTailCallerValuesCanBeCollectedWhileExecutionIsSuspended(string kind)
    {
        using var state = CreateState();
        var reference = InstallPayload(state);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Environment["pause"] = new LuaFunction(
            async (context, _) =>
            {
                await gate.Task;
                return context.Return();
            }
        );
        var source = kind switch
        {
            "arguments" => """
                local function worker() pause();return 42 end
                local function forward(...) return worker() end
                local value=payload
                payload=nil
                return forward(value)
                """,
            "closure" => """
                local function worker() pause();return 42 end
                local function make(value)
                    return function() if value then return worker() end end
                end
                local f=make(payload)
                payload=nil
                return f()
                """,
            _ => """
                local function sink() return 42 end
                local function make(value)
                    return function() if value then return sink() end end
                end
                local f=make(payload)
                payload=nil
                f()
                f=nil
                pause()
                return 42
                """,
        };
        var execution = state.DoStringAsync(source).AsTask();
        try
        {
            Assert.That(execution.IsCompleted, Is.False);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.That(reference.IsAlive, Is.False);
        }
        finally
        {
            gate.SetResult();
            await execution;
        }
        Assert.That(execution.Result, Is.EqualTo(new LuaValue[] { 42 }));
        Assert.That(state.GetStackValues().ToArray(), Is.EqualTo(new LuaValue[] { 101, 102 }));
        GC.KeepAlive(state);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailureClosesCapturesInReusedArgumentArea(bool asynchronous)
    {
        using var state = CreateState();
        state.Environment["fail"] = new LuaFunction(
            async (_, _) =>
            {
                if (asynchronous)
                    await Task.Yield();
                throw new InvalidOperationException("failure");
            }
        );
        var function = (
            await state.DoStringAsync(
                """
                return function(...)
                    local function worker()
                        local x=42
                        saved=function() return x end
                        fail()
                    end
                    return worker()
                end
                """
            )
        )[0].Read<LuaClosure>();
        Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.CallAsync(function, [11, 12, 13])
        );
        var saved = state.Environment["saved"].Read<LuaClosure>();
        Assert.That(saved.UpValues[0].IsClosed, Is.True);
        await state.DoStringAsync("local a,b,c,d=1,2,3,4;return a+b+c+d");
        Assert.That(await state.ExecuteAsync(saved), Is.EqualTo(new LuaValue[] { 42 }));
        Assert.That(state.GetStackValues().ToArray(), Is.EqualTo(new LuaValue[] { 101, 102 }));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference InstallPayload(LuaState state)
    {
        var payload = new object();
        state.Environment["payload"] = LuaValue.FromObject(payload);
        return new WeakReference(payload);
    }
}
