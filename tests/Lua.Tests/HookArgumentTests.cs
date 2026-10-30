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
            if thread then
                debug.sethook(co,hook,'c')
                assert(debug.gethook(co)==hook)
                debug.sethook(co,nil,{}, {})
                assert(debug.gethook(co)==nil)
            else
                debug.sethook(hook,'c')
                assert(debug.gethook()==hook)
                debug.sethook(nil,{}, {})
                assert(debug.gethook()==nil)
            end
            """
        );
        Assert.That(state.Stack.Count, Is.EqualTo(4));
        Assert.That(state.CallStackFrameCount, Is.Zero);
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
