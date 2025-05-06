using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

public class LuaApiTests
{
    LuaState state = default!;

    [OneTimeSetUp]
    public void SetUp()
    {
        state = LuaState.Create();
        state.OpenStandardLibraries();
    }

    [Test]
    public async Task TestArithmetic()
    {
        var source = """
                     metatable = {
                         __add = function(a, b)
                             local t = { }
                             for i = 1, #a do
                                 t[i] = a[i] + b[i]
                             end
                             return t
                         end
                     }

                     local a = { 1, 2, 3 }
                     local b = { 4, 5, 6 }

                     setmetatable(a, metatable)
                     return a, b
                     """;
        var result = await state.DoStringAsync(source);
        var a = result[0].Read<LuaTable>();
        var b = result[1].Read<LuaTable>();

        var c = await state.MainThread.OpArithmetic(a, b, OpCode.Add);
        var table = c.Read<LuaTable>();
        Assert.Multiple(() =>
        {
            Assert.That(table[1].Read<double>(), Is.EqualTo(5));
            Assert.That(table[2].Read<double>(), Is.EqualTo(7));
            Assert.That(table[3].Read<double>(), Is.EqualTo(9));
        });
    }

    [Test]
    public async Task TestUnary()
    {
        var source = """
                     metatable = {
                         __unm = function(a)
                             local t = { }
                             for i = 1, #a do
                                 t[i] = -a[i]
                             end
                             return t
                         end
                     }

                     local a = { 1, 2, 3 }

                     setmetatable(a, metatable)
                     return a
                     """;
        var result = await state.DoStringAsync(source);
        var a = result[0].Read<LuaTable>();

        var c = await state.MainThread.OpUnary(a, OpCode.Unm);
        var table = c.Read<LuaTable>();
        Assert.Multiple(() =>
        {
            Assert.That(table[1].Read<double>(), Is.EqualTo(-1));
            Assert.That(table[2].Read<double>(), Is.EqualTo(-2));
            Assert.That(table[3].Read<double>(), Is.EqualTo(-3));
        });
    }

    [Test]
    public async Task TestCompare()
    {
        var source = """
                     metatable = {
                         __eq = function(a, b)
                             if(#a ~= #b) then
                                 return false
                             end
                             for i = 1, #a do
                                if(a[i] ~= b[i]) then
                                    return  false
                                end
                             end
                             return true
                         end
                     }

                     local a = { 1, 2, 3 }
                     local b = { 4, 5, 6 }
                     local c = { 1, 2, 3 }
                     setmetatable(a, metatable)
                     return a, b, c
                     """;
        var result = await state.DoStringAsync(source);
        var a = result[0].Read<LuaTable>();
        var b = result[1].Read<LuaTable>();
        var c = result[2].Read<LuaTable>();
        var ab = await state.MainThread.OpCompare(a, b, OpCode.Eq);
        Assert.False(ab);
        var ac = await state.MainThread.OpCompare(a, c, OpCode.Eq);
        Assert.True(ac);
    }

    [Test]
    public async Task TestGetTable()
    {
        var source = """
                     metatable = {
                         __index = {x=1}
                     }

                     local a = {}
                     setmetatable(a, metatable)
                     return a
                     """;
        var result = await state.DoStringAsync(source);
        var a = result[0].Read<LuaTable>();
        Assert.That(await state.MainThread.OpGetTable(a, "x"), Is.EqualTo(new LuaValue(1)));
        a.Metatable!["__index"] = state.DoStringAsync("return function(a,b) return b end").Result[0];
        Assert.That(await state.MainThread.OpGetTable(a, "x"), Is.EqualTo(new LuaValue("x")));
    }

    [Test]
    public async Task TestSetTable()
    {
        var source = """
                     metatable = {
                         __newindex = {}
                     }

                     local a = {}
                     a.x = 1
                     setmetatable(a, metatable)
                     return a
                     """;
        var result = await state.DoStringAsync(source);
        var a = result[0].Read<LuaTable>();
        await state.MainThread.OpSetTable(a, "a", "b");
        var b = a.Metatable!["__newindex"].Read<LuaTable>()["a"];
        Assert.True(b.Read<string>() == "b");
    }

    [Test]
    public async Task Test_Metamethod_Concat()
    {
        var source = @"
metatable = {
    __concat = function(a, b)
        local t = { }
        for i = 1, #a do
            table.insert(t, a[i])
        end
        for i = 1, #b do
            table.insert(t, b[i])
        end
        return t
    end
}

local a = { 1, 2, 3 }
local b = { 4, 5, 6 }
local c = { 7, 8, 9 }
setmetatable(a, metatable)
setmetatable(c, metatable)

return a,b,c
";

        var result = await state.DoStringAsync(source);
        Assert.That(result, Has.Length.EqualTo(3));

        var a = result[0];
        var b = result[1];
        var c = result[2];
        var d = await state.MainThread.OpConcat([a, b, c]);

        var table = d.Read<LuaTable>();
        Assert.That(table.ArrayLength, Is.EqualTo(9));
        Assert.Multiple(() =>
        {
            Assert.That(table[1].Read<double>(), Is.EqualTo(1));
            Assert.That(table[2].Read<double>(), Is.EqualTo(2));
            Assert.That(table[3].Read<double>(), Is.EqualTo(3));
            Assert.That(table[4].Read<double>(), Is.EqualTo(4));
            Assert.That(table[5].Read<double>(), Is.EqualTo(5));
            Assert.That(table[6].Read<double>(), Is.EqualTo(6));
            Assert.That(table[7].Read<double>(), Is.EqualTo(7));
            Assert.That(table[8].Read<double>(), Is.EqualTo(8));
            Assert.That(table[9].Read<double>(), Is.EqualTo(9));
        });
    }

    [Test]
    public async Task Test_Metamethod_MetaCallViaMeta()
    {
        var source = """
                     local a = {name ="a"}
                     setmetatable(a, {
                         __call = function(a, b, c)
                             return a.name..b.name..c.name
                         end
                     })


                     local b = setmetatable({name="b"},
                       {__unm = a,
                       __add= a,
                       __concat =a
                       
                       })
                     local c ={name ="c"}
                     return b,c
                     """;
        var result = await state.DoStringAsync(source);
        var b = result[0];
        var c = result[1];
        var d = await state.MainThread.OpArithmetic(b, c, OpCode.Add);
        Assert.True(d.TryRead(out string s));
        Assert.That(s, Is.EqualTo("abc"));
        d = await state.MainThread.OpUnary(b, OpCode.Unm);
        Assert.True(d.TryRead(out s));
        Assert.That(s, Is.EqualTo("abb"));
        d = await state.MainThread.OpConcat([c, b]);
        Assert.True(d.TryRead(out s));
        Assert.That(s, Is.EqualTo("acb"));
    }
}