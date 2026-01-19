using Lua.Platforms;
using Lua.Standard;
using Lua.Tests.Helpers;
using System.Globalization;

namespace Lua.Tests;

[LuaObject]
partial class MetaFloat
{
    [LuaMember("value")] public double Value { get; set; }

    [LuaMetamethod(LuaObjectMetamethod.Call)]
    public static MetaFloat Create(LuaValue dummy, double value)
    {
        return new MetaFloat() { Value = value };
    }

    [LuaMetamethod(LuaObjectMetamethod.Add)]
    public static MetaFloat Add(MetaFloat a, MetaFloat b)
    {
        return new MetaFloat() { Value = a.Value + b.Value };
    }

    [LuaMetamethod(LuaObjectMetamethod.Lt)]
    public static bool Lt(MetaFloat a, MetaFloat b)
    {
        return a.Value < b.Value;
    }

    [LuaMetamethod(LuaObjectMetamethod.Len)]
    public static double Len(MetaFloat a)
    {
        return a.Value;
    }

    [LuaMetamethod(LuaObjectMetamethod.ToString)]
    public override string ToString()
    {
        return Value.ToString(CultureInfo.InvariantCulture);
    }
}

[LuaObject]
partial class MetaAsyncFloat
{
    [LuaMember("value")] public double Value { get; set; }

    [LuaMetamethod(LuaObjectMetamethod.Call)]
    public static async Task<MetaFloat> Create(LuaValue dummy, double value)
    {
        await Task.Delay(1);
        return new MetaFloat() { Value = value };
    }

    [LuaMetamethod(LuaObjectMetamethod.Add)]
    public static async Task<MetaFloat> Add(MetaFloat a, MetaFloat b)
    {
        await Task.Delay(1);
        return new MetaFloat() { Value = a.Value + b.Value };
    }

    [LuaMetamethod(LuaObjectMetamethod.Lt)]
    public static async Task<bool> Lt(MetaFloat a, MetaFloat b)
    {
        await Task.Delay(1);
        return a.Value < b.Value;
    }

    [LuaMetamethod(LuaObjectMetamethod.Len)]
    public static async Task<double> Len(MetaFloat a)
    {
        await Task.Delay(1);
        return a.Value;
    }

    [LuaMetamethod(LuaObjectMetamethod.ToString)]
    public override string ToString()
    {
        return Value.ToString(CultureInfo.InvariantCulture);
    }
}

public class MetaTests
{
    const string TestFloatScript = """
                                   local a = MetaFloat(10)
                                   local b = MetaFloat(20)
                                   local function test(x,y)
                                     local z = x + y
                                     local v = z.value
                                     local len, str = #z, tostring(z)
                                     return v, y > x ,len, str
                                   end
                                   local v, comp, len, str = test(a,b)
                                   assert(v == 30)
                                   assert(comp == true)
                                   assert(len == 30)
                                   assert(str == "30")
                                   local c = a + b
                                   return c.value, a < b,#c, tostring(c)
                                   """;

    const string TestIndexScript = """
                                   local obj = setmetatable({}, {__index = getindentityMethod})
                                   local a
                                   local b = 1
                                   local c =obj
                                   a = obj:getindentity()
                                   local d = obj.getindentity(obj)
                                   print(d)
                                   assert(a == obj)
                                   assert(b == 1)
                                   assert(obj == c)
                                   assert(d == c)
                                   """;

    [Test]
    public async Task TestMetaFloat()
    {
        var lua = LuaState.Create();
        lua.OpenBasicLibrary();
        lua.Environment["MetaFloat"] = new MetaFloat();
        var result = await lua.DoStringAsync(TestFloatScript);
        Assert.That(result.Length, Is.EqualTo(4));
        Assert.That(result[0].Read<double>(), Is.EqualTo(30));
        Assert.That(result[1].Read<bool>(), Is.EqualTo(true));
        Assert.That(result[2].Read<double>(), Is.EqualTo(30));
        Assert.That(result[3].Read<string>(), Is.EqualTo("30"));
    }

    [Test]
    public async Task TestMetaAsyncFloat()
    {
        var lua = LuaState.Create();
        lua.OpenBasicLibrary();
        lua.Environment["MetaFloat"] = new MetaAsyncFloat();

        var result = await lua.DoStringAsync(TestFloatScript);
        Assert.That(result.Length, Is.EqualTo(4));
        Assert.That(result[0].Read<double>(), Is.EqualTo(30));
        Assert.That(result[1].Read<bool>(), Is.EqualTo(true));
        Assert.That(result[2].Read<double>(), Is.EqualTo(30));
        Assert.That(result[3].Read<string>(), Is.EqualTo("30"));
    }

    [Test]
    public async Task TestMetaIndex()
    {
        var lua = LuaState.Create(LuaPlatform.Default with { StandardIO = new TestStandardIO() });
        lua.OpenBasicLibrary();

        lua.Environment["getindentityMethod"] = new LuaFunction("getindentity", (context, ct) =>
        {
            var obj = context.GetArgument(0);
            return new(context.Return(new LuaFunction("getIndexed",
                (ctx, ct2) =>
                    new(ctx.Return(obj)))));
        });


        var result = await lua.DoStringAsync(TestIndexScript);
    }

    [Test]
    public async Task TestMetaIndexAsync()
    {
        var lua = LuaState.Create(LuaPlatform.Default with { StandardIO = new TestStandardIO() });
        lua.OpenBasicLibrary();


        lua.Environment["getindentityMethod"] = new LuaFunction("getindentity", async (context, ct) =>
        {
            var obj = context.GetArgument(0);
            await Task.Delay(1);
            return (context.Return(new LuaFunction("getIndexed", (ctx, ct2)
                => new(ctx.Return(obj)))));
        });


        var result = await lua.DoStringAsync(TestIndexScript);
    }
}