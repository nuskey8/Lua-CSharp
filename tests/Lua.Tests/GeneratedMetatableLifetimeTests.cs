using System.Runtime.CompilerServices;
using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

[LuaObject]
public partial class GeneratedMetatableTestObject
{
    [LuaMember]
    public int Value { get; set; }

    [LuaMember]
    public int Read() => Value;
}

[LuaObject]
public partial record GeneratedMetatableTestRecord
{
    [LuaMember]
    public int Value { get; init; }
}

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class GeneratedMetatableLifetimeTests(LuaStringMode mode)
{
    LuaState CreateState()
    {
        var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        return state;
    }

    [TestCase("mt.__index=function() return 99 end")]
    [TestCase("mt.__index=nil")]
    [TestCase("mt.owner=coroutine.running()")]
    [TestCase("rawset(mt,'__index',nil)")]
    [TestCase("rawset(mt,'payload',{})")]
    [TestCase("setmetatable(mt,{})")]
    [TestCase("debug.setmetatable(mt,{})")]
    public async Task DefaultMetatableRejectsMutation(string mutation)
    {
        using var state = CreateState();
        state.Environment["obj"] = new GeneratedMetatableTestObject { Value = 7 };
        var result = await state.DoStringAsync(
            "local mt=getmetatable(obj);local ok,err=pcall(function() "
                + mutation
                + " end);return ok,type(err),obj:Read()"
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { false, "string", 7 }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DiscardedMetatablesDoNotRootStatesOrPayloads(bool replacement)
    {
        var (state, payload) = CreateAndDiscard(replacement);
        Collect();
        Assert.That(state.IsAlive, Is.False);
        Assert.That(payload.IsAlive, Is.False);
    }

    [Test]
    public void LiveHostUserDataDoesNotRootDiscardedStateDefaults()
    {
        var userData = new GeneratedMetatableTestObject();
        var (state, payload) = CreateAndDiscard(false, userData);
        Collect();
        Assert.That(state.IsAlive, Is.False);
        Assert.That(payload.IsAlive, Is.False);
        GC.KeepAlive(userData);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    (WeakReference State, WeakReference Payload) CreateAndDiscard(
        bool replacement,
        GeneratedMetatableTestObject? userData = null
    )
    {
        using var state = CreateState();
        var payload = new object();
        state.Environment["obj"] = userData ?? new GeneratedMetatableTestObject();
        state.Environment["payload"] = LuaValue.FromObject(payload);
        state
            .DoStringAsync(
                replacement
                    ? "debug.setmetatable(obj,{owner=coroutine.running(),payload=payload});obj=nil;payload=nil"
                    : """
                    local mt=getmetatable(obj)
                    assert(not pcall(function() mt.owner=coroutine.running() end))
                    assert(not pcall(function() rawset(mt,'payload',payload) end))
                    obj=nil;payload=nil
                    """
            )
            .AsTask()
            .GetAwaiter()
            .GetResult();
        return (new(state), new(payload));
    }

    [Test]
    public async Task IndependentStatesShareFrozenDefaults()
    {
        using var first = CreateState();
        using var second = CreateState();
        first.Environment["obj"] = new GeneratedMetatableTestObject();
        second.Environment["obj"] = new GeneratedMetatableTestObject();
        var firstTable = (await first.DoStringAsync("return getmetatable(obj)"))[0]
            .Read<LuaTable>();
        var secondTable = (await second.DoStringAsync("return getmetatable(obj)"))[0]
            .Read<LuaTable>();
        Assert.That(firstTable, Is.SameAs(secondTable));
        Assert.That(firstTable.IsReadOnly, Is.True);
    }

    [Test]
    public async Task ThreadsShareDefaultsAndReplacementAffectsOnlyOneUserData()
    {
        using var state = CreateState();
        state.Environment["first"] = new GeneratedMetatableTestObject { Value = 7 };
        state.Environment["second"] = new GeneratedMetatableTestObject { Value = 8 };
        var result = await state.DoStringAsync(
            """
            local shared=getmetatable(first)
            assert(shared==getmetatable(second))
            local co=coroutine.create(function()
                return getmetatable(second)==shared,second:Read()
            end)
            local ok,same,value=coroutine.resume(co)
            local replacement={__index=function() return 99 end}
            debug.setmetatable(first,replacement)
            local isolated=getmetatable(first)==replacement and getmetatable(second)==shared
            replacement.tag=42
            rawset(replacement,'__index',function() return replacement.tag end)
            local changed=first.Value
            debug.setmetatable(first,nil)
            local removed=getmetatable(first)==nil
            debug.setmetatable(first,shared)
            first.Value=9
            return ok,same,value,isolated,changed,removed,first:Read(),second.Value
            """
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { true, true, 8, true, 42, true, 9, 8 }));
    }

    [Test]
    public void MetatableOverridesDoNotChangeRecordEquality()
    {
        var first = new GeneratedMetatableTestRecord { Value = 7 };
        var second = new GeneratedMetatableTestRecord { Value = 7 };
        ILuaUserData userData = first;
        userData.Metatable = new LuaTable();
        Assert.That(first, Is.EqualTo(second));
        Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
    }

    static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
