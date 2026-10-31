using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class DebugLocalMetadataTests(LuaStringMode mode)
{
    [TestCase("1", false)]
    [TestCase(" 1.9 ", false)]
    [TestCase("0x1", false)]
    [TestCase("-1", false)]
    [TestCase("1", true)]
    public async Task GetInfoCoercesNumericStringLevels(string index, bool explicitThread)
    {
        using var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        state.Environment["index"] = index;
        state.Environment["thread"] = state;
        var prefix = explicitThread ? "thread," : "";
        await state.DoStringAsync(
            $$"""
            local a,b=debug.getinfo({{prefix}}index,'flS'),debug.getinfo({{prefix}}tonumber(index),'flS')
            if a==nil then assert(b==nil) else
                assert(a.func==b.func and a.currentline==b.currentline and a.source==b.source)
            end
            assert(not pcall(debug.getinfo,'not a level'))
            """
        );
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task WrappedCoroutineCanInspectItsParentsNativeFrame(bool asynchronous)
    {
        using var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        state.Environment["pause"] = new LuaFunction(
            async (context, _) =>
            {
                if (asynchronous)
                    await Task.Yield();
                return context.Return();
            }
        );
        var result = await state.DoStringAsync(
            """
            local main=coroutine.running()
            local run=coroutine.wrap(function(value)
                pause()
                local name,before=debug.getlocal(main,0,1)
                local changed=debug.setlocal(main,0,1,99)
                local info=debug.getinfo(main,0,'S')
                coroutine.yield(name,before,changed,info.what,value)
                return value
            end)
            local name,before,changed,what,value=run(42)
            return name,before,changed,what,value,run()
            """
        );
        Assert.That(
            result,
            Is.EqualTo(new LuaValue[] { "(*temporary)", 42, "(*temporary)", "C#", 42, 42 })
        );
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [Test]
    public async Task NativeCallerLocalLookupStopsBeforeLuaVarargs()
    {
        using var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        var probe = (
            await state.DoStringAsync(
                """
                return function(...)
                    local name,value=debug.getlocal(2,2)
                    local changed=debug.setlocal(2,2,99)
                    local validName,validValue=debug.getlocal(2,1)
                    return name,value,changed,validName,validValue,...
                end
                """
            )
        )[0].Read<LuaClosure>();
        state.Environment["bridge"] = new LuaFunction(
            async (context, token) =>
            {
                context.State.Stack.PushRange([10, 20, 30]);
                var count = await context.State.RunAsync(probe, 3, token);
                LuaValue[] values;
                using (var results = context.State.ReadStack(count))
                    values = results.AsSpan().ToArray();
                return context.Return(values);
            }
        );
        var result = await state.DoStringAsync("return bridge(42)");
        Assert.That(
            result,
            Is.EqualTo(
                new LuaValue[]
                {
                    LuaValue.Nil,
                    LuaValue.Nil,
                    LuaValue.Nil,
                    "(*temporary)",
                    42,
                    10,
                    20,
                    30,
                }
            )
        );
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task CallerLocalLookupStopsBeforeCalleeArguments(bool callable, bool tailCall)
    {
        using var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        var parameters = callable ? "self,..." : "...";
        var forward = tailCall
            ? "local original=probe;probe=function("
                + parameters
                + ") return original("
                + parameters
                + ") end"
            : "";
        var target = callable ? "setmetatable({}, {__call=probe})" : "probe";
        var result = await state.DoStringAsync(
            $$"""
            local target
            do
                local function probe({{parameters}})
                    local name,value=debug.getlocal(2,4)
                    local changed=debug.setlocal(2,4,99)
                    local validName,validValue=debug.getlocal(2,2)
                    debug.setlocal(2,2,43)
                    return name,value,changed,validName,validValue,...
                end
                {{forward}}
                target={{target}}
            end
            local a=42
            local name,value,changed,validName,validValue,x,y,z=target(10,20,30)
            return name,value,changed,validName,validValue,x,y,z,a
            """
        );
        Assert.That(
            result,
            Is.EqualTo(
                new LuaValue[] { LuaValue.Nil, LuaValue.Nil, LuaValue.Nil, "a", 42, 10, 20, 30, 43 }
            )
        );
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LineHooksHandleStrippedChunks(bool asynchronous)
    {
        using var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        var prototype = state
            .Load("local total=0;for i=1,3 do total=total+i end;return total", "lines")
            .Proto;
        var stripped = state.Load(StripDebug(prototype).ToBytecode(), "stripped-lines");
        var lines = new List<int>();
        state.SetHook(
            new LuaFunction(
                async (context, _) =>
                {
                    if (asynchronous)
                        await Task.Yield();
                    Assert.That(context.GetArgument<string>(0), Is.EqualTo("line"));
                    lines.Add(context.GetArgument<int>(1));
                    return context.Return();
                }
            ),
            "l"
        );
        state.Stack.PushRange([101, 102]);
        var result = await state.ExecuteAsync(stripped);
        Assert.That(result, Is.EqualTo(new LuaValue[] { 6 }));
        Assert.That(lines.Count, Is.GreaterThan(1));
        Assert.That(lines, Is.All.EqualTo(0));
        Assert.That(state.GetStackValues().ToArray(), Is.EqualTo(new LuaValue[] { 101, 102 }));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LineLookupHandlesFunctionQueriesAndStrippedActiveFrames(bool stripped)
    {
        using var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        var original = (
            await state.DoStringAsync(
                "return function() local info=debug.getinfo(1,'l'); return info.currentline end"
            )
        )[0]
            .Read<LuaClosure>();
        state.Environment["f"] = stripped
            ? new LuaClosure(state, StripDebug(original.Proto))
            : original;
        var result = await state.DoStringAsync(
            "local info=debug.getinfo(f); local line=f(); return info.currentline,line"
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { -1, stripped ? 0 : 1 }));
    }

    [TestCase(0)]
    [TestCase(1)]
    public async Task LargeLocalIndicesReturnNilWithoutChangingLocals(int level)
    {
        using var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        state.Environment["level"] = level;
        var result = await state.DoStringAsync(
            """
            local function f()
                local x=42
                local name=debug.getlocal(level,2147483647)
                local changed=debug.setlocal(level,2147483647,99)
                return name,changed,x
            end
            local pad=0
            local a,b,c=f()
            return a,b,c
            """
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { LuaValue.Nil, LuaValue.Nil, 42 }));
    }

    static Prototype StripDebug(Prototype proto) =>
        new(
            proto.ChunkName,
            proto.LineDefined,
            proto.LastLineDefined,
            proto.ParameterCount,
            proto.MaxStackSize,
            proto.HasVariableArguments,
            proto.Constants.ToArray(),
            proto.Code.ToArray(),
            proto.ChildPrototypes.ToArray(),
            [],
            [],
            proto.UpValues.ToArray()
        );

    [TestCase(false)]
    [TestCase(true)]
    public async Task ParameterLookupHandlesStrippedDebugLocals(bool stripped)
    {
        using var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        var original = (
            await state.DoStringAsync("return function(a,b) local c=a+b; return c end")
        )[0]
            .Read<LuaClosure>();
        var proto = original.Proto;
        if (stripped)
        {
            proto = StripDebug(proto);
        }
        state.Environment["f"] = new LuaClosure(state, proto);
        var results = await state.DoStringAsync(
            "return debug.getlocal(f,1),debug.getlocal(f,2),debug.getlocal(f,3),debug.getlocal(f,0),debug.getlocal(f,-1)"
        );
        Assert.That(
            results,
            Is.EqualTo(
                new LuaValue[]
                {
                    stripped ? LuaValue.Nil : new LuaValue("a"),
                    stripped ? LuaValue.Nil : new LuaValue("b"),
                    LuaValue.Nil,
                    LuaValue.Nil,
                    LuaValue.Nil,
                }
            )
        );
    }
}
