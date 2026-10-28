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
            proto = new Prototype(
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
