using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class DebugLocalMetadataTests(LuaStringMode mode)
{
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
