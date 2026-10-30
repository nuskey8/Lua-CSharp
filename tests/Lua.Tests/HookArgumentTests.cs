using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class HookArgumentTests(LuaStringMode mode)
{
    LuaState CreateState()
    {
        var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        state.Stack.PushRange([101, 102, 103, 104]);
        return state;
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DisablingHooksIgnoresUnusedArguments(bool thread)
    {
        using var state = CreateState();
        state.Environment["thread"] = thread;
        await state.DoStringAsync(
            """
            local co=coroutine.create(function() end)
            local function hook() end
            local function checkDisabled(thread)
                local callback,mask,count=debug.gethook(thread)
                assert(callback==nil and mask=='' and count==0)
            end
            checkDisabled()
            checkDisabled(co)
            if thread then
                debug.sethook(co,hook,'c')
                assert(debug.gethook(co)==hook)
                debug.sethook(co,nil,{}, {})
                checkDisabled(co)
            else
                debug.sethook(hook,'c')
                assert(debug.gethook()==hook)
                debug.sethook(nil,{}, {})
                checkDisabled()
            end
            """
        );
        Assert.That(state.Stack.Count, Is.EqualTo(4));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    const string HookWorkload = """
        debug.sethook(function(event)
            if event==hookEvent then
                debug.sethook()
                work()
            end
        end,hookMask,hookCount)
        local function leaf() return 42 end
        local value=leaf()
        return leaf()
        """;

    static LuaClosure LoadHookWorkload(LuaState state, string hookEvent)
    {
        state.Environment["hookEvent"] = hookEvent;
        state.Environment["hookMask"] = hookEvent switch
        {
            "call" or "tail call" => "c",
            "return" => "r",
            "line" => "l",
            _ => "",
        };
        state.Environment["hookCount"] = hookEvent == "count" ? 1 : 0;
        return state.Load(HookWorkload, "@hook-workload.lua");
    }

    [TestCase("call")]
    [TestCase("tail call")]
    [TestCase("return")]
    [TestCase("line")]
    [TestCase("count")]
    public async Task LuaHooksCannotYieldAfterDisablingTheHook(string hookEvent)
    {
        using var state = CreateState();
        state.Environment["work"] = state.Environment["coroutine"].Read<LuaTable>()["yield"];
        state.Environment["operation"] = LoadHookWorkload(state, hookEvent);
        await state.DoStringAsync(
            """
            thread=coroutine.create(operation)
            local ok,e=coroutine.resume(thread)
            assert(not ok and e:find('attempt to yield across a C-call boundary',1,true))
            assert(coroutine.status(thread)=='dead')
            """
        );
        var thread = state.Environment["thread"].Read<LuaState>();
        Assert.That(thread.NonYieldableCallCount, Is.Zero);
        Assert.That(thread.CallStackFrameCount, Is.Zero);
        Assert.That(state.Stack.Count, Is.EqualTo(4));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase("call")]
    [TestCase("tail call")]
    [TestCase("return")]
    [TestCase("line")]
    [TestCase("count")]
    public async Task LuaHooksCanAwaitHostCallbacks(string hookEvent)
    {
        using var state = CreateState();
        var suspension = new ManualSuspension();
        var calls = 0;
        state.Environment["work"] = new LuaFunction(
            async (context, _) =>
            {
                calls++;
                await suspension;
                return context.Return();
            }
        );
        var task = state.ExecuteAsync(LoadHookWorkload(state, hookEvent)).AsTask();
        Assert.That(task.IsCompleted, Is.False);
        suspension.Resume();
        Assert.That(await task, Is.EqualTo(new LuaValue[] { 42 }));
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(state.NonYieldableCallCount, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
        Assert.That(state.Stack.Count, Is.EqualTo(4));
    }

    [TestCase("line")]
    [TestCase("count")]
    public async Task HostHooksCanStillYield(string hookEvent)
    {
        using var state = CreateState();
        using var thread = state.CreateCoroutine(
            state.Load("local value=42;return value", "@host-hook.lua")
        );
        thread.SetHook(
            new LuaFunction(
                async (context, token) =>
                {
                    context.State.SetHook(null, "");
                    await context.State.YieldAsync(context, token);
                    return context.Return();
                }
            ),
            hookEvent == "line" ? "l" : "",
            hookEvent == "count" ? 1 : 0
        );
        var output = new LuaStack();
        await thread.ResumeAsync(output);
        Assert.That(output.Get(0), Is.EqualTo(new LuaValue(true)));
        Assert.That(output.Get(1), Is.EqualTo(new LuaValue(hookEvent)));
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Suspended));
        output.Clear();
        await thread.ResumeAsync(output);
        Assert.That(output.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 42 }));
        Assert.That(thread.NonYieldableCallCount, Is.Zero);
        Assert.That(thread.CallStackFrameCount, Is.Zero);
    }

    [TestCase("")]
    [TestCase(",nil")]
    public async Task InstallingHooksRequiresAMask(string mask)
    {
        using var state = CreateState();
        await state.DoStringAsync(
            $$"""
            local original=function() end
            debug.sethook(original,'c')
            local ok,e=pcall(function() debug.sethook(function() end{{mask}}) end)
            assert(not ok and type(e)=='string')
            assert(debug.gethook()==original)
            debug.sethook()
            """
        );
    }

    [TestCase(0, "c")]
    [TestCase(1, "c")]
    [TestCase(2, "c")]
    [TestCase(3, "c")]
    [TestCase(0, "r")]
    [TestCase(3, "r")]
    [TestCase(0, "l")]
    [TestCase(3, "l")]
    [TestCase(0, "")]
    [TestCase(3, "")]
    public async Task LuaHooksReceiveFixedAndVariableArguments(int fixedParameters, string mask)
    {
        using var state = CreateState();
        var calls = new List<LuaValue[]>();
        state.Environment["observe"] = new LuaFunction(
            (context, _) =>
            {
                calls.Add(context.Arguments.ToArray());
                return new(context.Return());
            }
        );
        var parameters = fixedParameters switch
        {
            0 => "...",
            1 => "event,...",
            2 => "event,line,...",
            _ => "event,line,extra,...",
        };
        var hook = (
            await state.DoStringAsync(
                "return function(" + parameters + ") observe(" + parameters + ");return 99 end"
            )
        )[0]
            .Read<LuaClosure>();
        state.SetHook(hook, mask, mask.Length == 0 ? 1 : 0);

        var result = await state.DoStringAsync(
            "local function f(...) return ... end; local function g(...) return f(...) end; local x=g(42); return x"
        );
        state.SetHook(null, "");

        Assert.That(result, Is.EqualTo(new LuaValue[] { 42 }));
        Assert.That(calls, Is.Not.Empty);
        foreach (var call in calls)
        {
            Assert.That(call.Length, Is.EqualTo(Math.Max(2, fixedParameters)));
            Assert.That(
                call[0].Read<string>(),
                mask switch
                {
                    "c" => Is.EqualTo("call").Or.EqualTo("tail call"),
                    "r" => Is.EqualTo("return"),
                    "l" => Is.EqualTo("line"),
                    _ => Is.EqualTo("count"),
                }
            );
            if (mask == "l")
                Assert.That(call[1].Read<int>(), Is.GreaterThan(0));
            else
                Assert.That(call[1], Is.EqualTo(LuaValue.Nil));
            if (fixedParameters == 3)
                Assert.That(call[2], Is.EqualTo(LuaValue.Nil));
        }
        Assert.That(
            state.GetStackValues().ToArray(),
            Is.EqualTo(new LuaValue[] { 101, 102, 103, 104 })
        );
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LuaInstalledReturnHookReceivesVarargs(bool fixedEvent)
    {
        using var state = CreateState();
        var parameters = fixedEvent ? "event,..." : "...";
        var result = await state.DoStringAsync(
            "local seen; debug.sethook(function("
                + parameters
                + ") debug.sethook();seen=table.pack("
                + parameters
                + ") end,'r');return seen.n,seen[1],seen[2]"
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { 2, "return", LuaValue.Nil }));
        Assert.That(
            state.GetStackValues().ToArray(),
            Is.EqualTo(new LuaValue[] { 101, 102, 103, 104 })
        );
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }
}
