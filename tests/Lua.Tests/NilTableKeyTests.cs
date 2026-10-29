using System.Runtime.CompilerServices;
using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class NilTableKeyTests(LuaStringMode mode)
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task RawSetRejectsNilKeysWithoutChangingTheTable(bool nilValue)
    {
        using var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        state.Environment["nilValue"] = nilValue;
        var result = await state.DoStringAsync(
            """
            local t={anchor=42}
            local value
            if not nilValue then value={} end
            local ok,message=pcall(rawset,t,nil,value)
            local count=0
            for k,v in pairs(t) do count=count+1 end
            return ok,type(message),t.anchor,count
            """
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { false, "string", 42, 1 }));
    }

    [Test]
    public void RejectedHostWriteDoesNotKeepItsValueAlive()
    {
        var table = new LuaTable();
        var payload = RejectWrite(table);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.That(payload.IsAlive, Is.False);
        Assert.That(table.HashMapCount, Is.Zero);
        GC.KeepAlive(table);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference RejectWrite(LuaTable table)
    {
        var payload = new object();
        Assert.Throws<ArgumentException>(() => table[LuaValue.Nil] = LuaValue.FromObject(payload));
        return new WeakReference(payload);
    }
}
