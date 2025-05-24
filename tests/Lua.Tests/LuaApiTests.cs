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
        var access = state.TopLevelAccess;
        var result = await access.DoStringAsync(source);
        var a = result[0].Read<LuaTable>();
        var b = result[1].Read<LuaTable>();

        var c = await access.Add(a, b);
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
        var access = state.TopLevelAccess;

        var result = await access.DoStringAsync(source);
        var a = result[0].Read<LuaTable>();
        var c = await access.Unm(a);
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
        var access = state.TopLevelAccess;
        var result = await access.DoStringAsync(source);
        var a = result[0].Read<LuaTable>();
        var b = result[1].Read<LuaTable>();
        var c = result[2].Read<LuaTable>();
        var ab = await access.Equals(a, b);
        Assert.False(ab);
        var ac = await access.Equals(a, c);
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
        var access = state.TopLevelAccess;
        var result = await access.DoStringAsync(source);
        var a = result[0].Read<LuaTable>();
        Assert.That(await access.GetTable(a, "x"), Is.EqualTo(new LuaValue(1)));
        a.Metatable!["__index"] = state.DoStringAsync("return function(a,b) return b end").Result[0];
        Assert.That(await access.GetTable(a, "x"), Is.EqualTo(new LuaValue("x")));
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
        var access = state.TopLevelAccess;
        var result = await access.DoStringAsync(source);
        var a = result[0].Read<LuaTable>();
        await access.SetTable(a, "a", "b");
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
        var access = state.TopLevelAccess;
        var result = await access.DoStringAsync(source);
        Assert.That(result, Has.Length.EqualTo(3));

        var a = result[0];
        var b = result[1];
        var c = result[2];
        var d = await access.Concat([a, b, c]);

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
                     return a,b,c
                     """;
        var access = state.TopLevelAccess;
        var result = await access.DoStringAsync(source);
        var a = result[0];
        var b = result[1];
        var c = result[2];
        var d = await access.Add(b, c);
        Assert.True(d.TryRead(out string s));
        Assert.That(s, Is.EqualTo("abc"));
        d = await access.Unm(b);
        Assert.True(d.TryRead(out s));
        Assert.That(s, Is.EqualTo("abb"));
        d = await access.Concat([c, b]);
        Assert.True(d.TryRead(out s));
        Assert.That(s, Is.EqualTo("acb"));

        var aResult = await access.Call(a, [b, c]);
        Assert.That(aResult, Has.Length.EqualTo(1));
        Assert.That(aResult[0].Read<string>(), Is.EqualTo("abc"));
    }

    [Test]
    public async Task Test_Metamethod_MetaCallViaMeta_VarArg()
    {
        var source = """
                     local a = {name ="a"}
                     setmetatable(a, {
                         __call = function(a, ...)
                            local args = {...}
                            local b,c =args[1],args[2]
                            return a.name..b.name..c.name
                         end
                     })


                     local b = setmetatable({name="b"},
                       {__unm = a,
                       __add= a,
                       __concat =a
                       
                       })
                     local c ={name ="c"}
                     return a,b,c
                     """;
        var access = state.TopLevelAccess;
        var result = await access.DoStringAsync(source);
        var a = result[0];
        var b = result[1];
        var c = result[2];
        var d = await access.Add(b, c);
        Assert.True(d.TryRead(out string s));
        Assert.That(s, Is.EqualTo("abc"));
        d = await access.Unm(b);
        Assert.True(d.TryRead(out s));
        Assert.That(s, Is.EqualTo("abb"));
        d = await access.Concat([c, b]);
        Assert.True(d.TryRead(out s));
        Assert.That(s, Is.EqualTo("acb"));

        var aResult = await access.Call(a, [b, c]);
        Assert.That(aResult, Has.Length.EqualTo(1));
        Assert.That(aResult[0].Read<string>(), Is.EqualTo("abc"));
    }
}