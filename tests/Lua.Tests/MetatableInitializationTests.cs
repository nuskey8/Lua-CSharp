using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

[LuaObject]
public partial class ConcurrentMetatableTestObject
{
    [LuaMember]
    public int Value { get; set; }

    [LuaMember]
    public int Read() => Value;

    [LuaMetamethod(LuaObjectMetamethod.Add)]
    public static int Add(ConcurrentMetatableTestObject a, ConcurrentMetatableTestObject b) =>
        a.Value + b.Value;

    [LuaMetamethod(LuaObjectMetamethod.Len)]
    public int Length() => Value;

    [LuaMetamethod(LuaObjectMetamethod.Call)]
    public int Call() => Value;

    [LuaMetamethod(LuaObjectMetamethod.ToString)]
    public override string ToString() => Value.ToString();
}

public class MetatableInitializationTests
{
    [Test]
    public async Task ParallelStatesInitializeCompleteIndependentMetatables()
    {
        const int workerCount = 8;
        using var barrier = new Barrier(workerCount);
        var workers = Enumerable
            .Range(0, workerCount)
            .Select(worker =>
                Task.Factory.StartNew(
                    () =>
                    {
                        using var state = LuaState.Create();
                        state.OpenStandardLibraries();
                        state.Environment["obj"] = new ConcurrentMetatableTestObject();
                        var script = state.Load(
                            "obj.Value=42;return obj:Read(),#obj,obj(),obj+obj,tostring(obj),getmetatable(obj)",
                            "parallel-metatable.lua"
                        );
                        Assert.That(barrier.SignalAndWait(TimeSpan.FromSeconds(10)), Is.True);
                        var results = state.ExecuteAsync(script).GetAwaiter().GetResult();
                        Assert.That(
                            results[..5],
                            Is.EqualTo(new LuaValue[] { 42, 42, 42, 84, "42" })
                        );
                        var metatable = results[5].Read<LuaTable>();
                        foreach (
                            var key in new[]
                            {
                                "__index",
                                "__newindex",
                                "__add",
                                "__len",
                                "__call",
                                "__tostring",
                            }
                        )
                            Assert.That(metatable[key].Type, Is.EqualTo(LuaValueType.Function));
                        return metatable;
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                )
            )
            .ToArray();
        var tables = await Task.WhenAll(workers);
        Assert.That(tables.Distinct().Count(), Is.EqualTo(workerCount));
    }

    [Test]
    public async Task HostReplacementAndRemovalAffectOnlyOneUserData()
    {
        using var state = LuaState.Create();
        var obj = new ConcurrentMetatableTestObject { Value = 7 };
        ILuaUserData first = obj;
        ILuaUserData second = new ConcurrentMetatableTestObject();
        var original = first.Metatable!;
        Assert.That(second.Metatable, Is.Not.SameAs(original));
        var replacement = new LuaTable();
        replacement[Metamethods.Index] = new LuaFunction((context, ct) => new(context.Return(99)));
        first.Metatable = replacement;
        Assert.That(second.Metatable, Is.Not.SameAs(replacement));
        state.Environment["obj"] = obj;
        Assert.That((await state.DoStringAsync("return obj.Value"))[0].Read<int>(), Is.EqualTo(99));
        first.Metatable = null;
        Assert.That(first.Metatable, Is.Null);
        Assert.That(first.GetMetatable(state), Is.Null);
        first.Metatable = original;
        Assert.That((await state.DoStringAsync("return obj:Read()"))[0].Read<int>(), Is.EqualTo(7));
    }
}
