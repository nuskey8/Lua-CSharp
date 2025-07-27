using Lua.Standard;

namespace Lua.Tests;

public class MetatableTests
{
    LuaGlobalState globalState = default!;

    [OneTimeSetUp]
    public void SetUp()
    {
        globalState = LuaGlobalState.Create();
        globalState.OpenStandardLibraries();
    }

    [Test]
    public async Task Test_Metamethod_Add()
    {
        var source = @"
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

return a + b
";

        var result = await globalState.DoStringAsync(source);
        Assert.That(result, Has.Length.EqualTo(1));

        var table = result[0].Read<LuaTable>();
        Assert.Multiple(() =>
        {
            Assert.That(table[1].Read<double>(), Is.EqualTo(5));
            Assert.That(table[2].Read<double>(), Is.EqualTo(7));
            Assert.That(table[3].Read<double>(), Is.EqualTo(9));
        });
    }

    [Test]
    public async Task Test_Metamethod_Concat()
    {
        var source = @"
metatable = {
    __concat = function(a, b)
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

return a .. b
";

        var result = await globalState.DoStringAsync(source);
        Assert.That(result, Has.Length.EqualTo(1));

        var table = result[0].Read<LuaTable>();
        Assert.Multiple(() =>
        {
            Assert.That(table[1].Read<double>(), Is.EqualTo(5));
            Assert.That(table[2].Read<double>(), Is.EqualTo(7));
            Assert.That(table[3].Read<double>(), Is.EqualTo(9));
        });
    }

    [Test]
    public async Task Test_Metamethod_Index()
    {
        var source = @"
metatable = {
    __index = {x=1}
}

local a = {}
setmetatable(a, metatable)
assert(a.x == 1)
metatable.__index= nil
assert(a.x == nil)
metatable.__index= function(a,b) return b end
assert(a.x == 'x')
";
        await globalState.DoStringAsync(source);
    }

    [Test]
    public async Task Test_Metamethod_NewIndex()
    {
        var source = @"
metatable = {
    __newindex = {}
}

local a = {}
a.x = 1
setmetatable(a, metatable)
a.x = 2
assert(a.x == 2)
a.x = nil
a.x = 2
assert(a.x == nil)
assert(metatable.__newindex.x == 2)
";
        await globalState.DoStringAsync(source);
    }

    [Test]
    public async Task Test_Metamethod_Call()
    {
        var source = @"
metatable = {
    __call = function(a, b)
        return a.x + b
    end
}

local a = {}
a.x = 1
setmetatable(a, metatable)
assert(a(2) == 3)
function tail(a, b)
    return a(b)
end
tail(a, 3)
assert(tail(a, 3) == 4)
";
        await globalState.DoStringAsync(source);
    }

    [Test]
    public async Task Test_Metamethod_TForCall()
    {
        var source = @"
local i =3
function a(...)
  local v ={...}
   assert(v[1] ==t)
   assert(v[2] == nil)
   if i ==3 then
       assert(v[3] == nil)
    else
      assert(v[3] == i)
    end
   
   i  =i -1
   if i ==0 then return nil end
   return i
end

t =setmetatable({},{__call = a})

for i in t do 
end
";
        await globalState.DoStringAsync(source);
    }

    [Test]
    public async Task Test_Hook_Metamethods()
    {
        var source = """ 
                     local t = {}
                     local a =setmetatable({},{__add =function (a,b) return a end})

                     debug.sethook(function () table.insert(t,debug.traceback()) end,"c")
                     a =a+a
                     debug.sethook()
                     return t
                     """;
        var r = await globalState.DoStringAsync(source);
        Assert.That(r, Has.Length.EqualTo(1));
        Assert.That(r[0].Read<LuaTable>()[1].Read<string>(), Does.Contain("stack traceback:"));
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
                     assert((b + c)== "abc")
                     assert((b .. c)== "abc")
                     """;
        await globalState.DoStringAsync(source);
    }
}