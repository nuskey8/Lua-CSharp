using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

public class ReadOnlyTableTests
{
    [Test]
    public void FreezeRejectsHostWritesAndWritableViews()
    {
        var table = new LuaTable
        {
            [1] = 3,
            [2] = 1,
            [3] = 2,
            ["key"] = 7,
        };
        table.Freeze();
        TestDelegate[] writes =
        [
            () => table[1] = 9,
            () => table["key"] = LuaValue.Nil,
            () => table["new"] = 9,
            () => table.Insert(1, 9),
            () => table.RemoveAt(1),
            () => table.Clear(),
            () => table.Metatable = new LuaTable(),
            () => table.GetArraySpan(),
            () => table.GetArrayMemory(),
        ];
        foreach (var write in writes)
        {
            Assert.That(write, Throws.InvalidOperationException.With.Message.Contains("read-only"));
        }
        Assert.That(
            table.GetArrayReadOnlySpan()[..3].ToArray(),
            Is.EqualTo(new LuaValue[] { 3, 1, 2 })
        );
        Assert.That(table["key"], Is.EqualTo(new LuaValue(7)));
        Assert.That(table.ContainsKey("new"), Is.False);
        Assert.That(table.Metatable, Is.Null);
    }

    [Test]
    public void FreezeDetachesPreviouslyExposedArrayAndRecomputesLength()
    {
        var table = new LuaTable
        {
            [1] = 3,
            [2] = 1,
            [3] = 2,
        };
        Assert.That(table.ArrayLength, Is.EqualTo(3));
        var span = table.GetArraySpan();
        var memory = table.GetArrayMemory();
        span[1] = LuaValue.Nil;
        table.Freeze();
        span[0] = 99;
        memory.Span[1] = 42;
        table.Freeze();
        Assert.That(table.IsReadOnly, Is.True);
        Assert.That(table.ArrayLength, Is.EqualTo(1));
        Assert.That(
            table.GetArrayReadOnlySpan()[..3].ToArray(),
            Is.EqualTo(new LuaValue[] { 3, LuaValue.Nil, 2 })
        );
    }
}

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class ReadOnlyTableLuaTests(LuaStringMode mode)
{
    LuaState CreateState()
    {
        var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        return state;
    }

    [TestCase("t[1]=9")]
    [TestCase("t.key=9")]
    [TestCase("t[1]=nil")]
    [TestCase("t.new=9")]
    [TestCase("rawset(t,'key',nil)")]
    [TestCase("table.insert(t,2,9)")]
    [TestCase("table.remove(t,1)")]
    [TestCase("table.sort(t)")]
    public async Task LuaWritesCannotChangeFrozenTable(string mutation)
    {
        using var state = CreateState();
        var table = new LuaTable
        {
            [1] = 3,
            [2] = 1,
            [3] = 2,
            ["key"] = 7,
        };
        table.Freeze();
        state.Environment["t"] = table;
        var result = await state.DoStringAsync(
            "local ok,err=pcall(function() " + mutation + " end);return ok,tostring(err)"
        );
        Assert.That(result[0], Is.EqualTo(new LuaValue(false)));
        Assert.That(result[1].Read<string>(), Does.Contain("read-only"));
        Assert.That(
            table.GetArrayReadOnlySpan()[..3].ToArray(),
            Is.EqualTo(new LuaValue[] { 3, 1, 2 })
        );
        Assert.That(table["key"], Is.EqualTo(new LuaValue(7)));
        Assert.That(table.ContainsKey("new"), Is.False);
    }

    [TestCase(1)]
    [TestCase("key")]
    [TestCase("new")]
    public void HostSetTableCannotChangeFrozenTable(object key)
    {
        using var state = CreateState();
        var table = new LuaTable { [1] = 3, ["key"] = 7 };
        table.Freeze();
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await state.SetTableAsync(table, LuaValue.FromObject(key), 9)
        );
        Assert.That(table[1], Is.EqualTo(new LuaValue(3)));
        Assert.That(table["key"], Is.EqualTo(new LuaValue(7)));
        Assert.That(table.ContainsKey("new"), Is.False);
    }

    [Test]
    public async Task FrozenTablesSupportReadsAndModuleSearchers()
    {
        using var state = CreateState();
        var table = new LuaTable
        {
            [1] = 3,
            [2] = 1,
            [3] = 2,
        };
        table.Freeze();
        state.Environment["t"] = table;
        await state.DoStringAsync("package.preload.frozen=function() return 42 end");
        state.Environment["package"].Read<LuaTable>()["searchers"].Read<LuaTable>().Freeze();
        var result = await state.DoStringAsync(
            """
            local sum=0
            for k,v in pairs(t) do sum=sum+v end
            return #t,rawget(t,1),table.concat(t,','),sum,require('frozen'),table.unpack(t)
            """
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { 3, 3, "3,1,2", 6, 42, 3, 1, 2 }));
    }

    [Test]
    public async Task OrdinaryLuaTablesAndMetatablesRemainMutable()
    {
        using var state = CreateState();
        var result = await state.DoStringAsync(
            """
            local t=setmetatable({3,1,2},{tag=1})
            getmetatable(t).tag=42
            rawset(t,'key',7)
            table.insert(t,4)
            local removed=table.remove(t,1)
            table.sort(t)
            debug.setmetatable(t,{__index=function() return 9 end})
            return removed,table.concat(t,','),t.key,t.missing
            """
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { 3, "1,2,4", 7, 9 }));
    }
}
