using System.Collections.Concurrent;
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
    public void ParallelFirstAccess_PublishesOneCompleteMetatable()
    {
        const int workerCount = 8;
        const int rounds = 500;
        using var barrier = new Barrier(workerCount);
        var failures = new ConcurrentQueue<string>();
        var tables = new LuaTable?[workerCount];
        ILuaUserData resetter = new ConcurrentMetatableTestObject();
        var workers = Enumerable
            .Range(0, workerCount)
            .Select(worker => new Thread(() =>
            {
                try
                {
                    using var state = LuaState.Create();
                    state.OpenStandardLibraries();
                    var obj = new ConcurrentMetatableTestObject();
                    ILuaUserData userData = obj;
                    state.Environment["obj"] = obj;
                    var script = state.Load(
                        "obj.Value = 42; return obj:Read(), #obj, obj(), obj + obj, tostring(obj)",
                        "parallel-metatable.lua"
                    );
                    for (var round = 0; round < rounds; round++)
                    {
                        if (worker == 0)
                        {
                            // Reset only between rounds, after all readers have finished.
                            resetter.Metatable = null;
                            Array.Clear(tables);
                        }
                        barrier.SignalAndWait(TimeSpan.FromSeconds(10));
                        try
                        {
                            var table = userData.Metatable!;
                            tables[worker] = table;
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
                            {
                                if (table[key].Type != LuaValueType.Function)
                                    failures.Enqueue(
                                        $"Round {round}, worker {worker}: incomplete {key}"
                                    );
                            }
                            var results = state.ExecuteAsync(script).GetAwaiter().GetResult();
                            if (
                                results.Length != 5
                                || results[0].Read<int>() != 42
                                || results[1].Read<int>() != 42
                                || results[2].Read<int>() != 42
                                || results[3].Read<int>() != 84
                                || results[4].Read<string>() != "42"
                            )
                                failures.Enqueue(
                                    $"Round {round}, worker {worker}: incorrect Lua result"
                                );
                        }
                        catch (Exception ex)
                        {
                            failures.Enqueue($"Round {round}, worker {worker}: {ex.Message}");
                        }
                        barrier.SignalAndWait(TimeSpan.FromSeconds(10));
                        if (worker == 0 && tables.Any(table => !ReferenceEquals(table, tables[0])))
                            failures.Enqueue($"Round {round}: readers used different metatables");
                    }
                }
                catch (Exception ex)
                {
                    failures.Enqueue($"Worker {worker}: {ex.Message}");
                }
            })
            {
                IsBackground = true,
            })
            .ToArray();
        foreach (var worker in workers)
            worker.Start();
        foreach (var worker in workers)
            Assert.That(worker.Join(TimeSpan.FromSeconds(30)), Is.True, "Worker did not finish");
        Assert.That(failures, Is.Empty, string.Join("\n", failures.Take(10)));
    }

    [Test]
    public async Task ReplacementAndReset_PreserveSharedMetatableBehavior()
    {
        using var state = LuaState.Create();
        var obj = new ConcurrentMetatableTestObject { Value = 7 };
        ILuaUserData first = obj;
        ILuaUserData second = new ConcurrentMetatableTestObject();
        first.Metatable = null;
        var original = first.Metatable!;
        Assert.That(second.Metatable, Is.SameAs(original));
        var replacement = new LuaTable();
        replacement[Metamethods.Index] = new LuaFunction((context, ct) => new(context.Return(99)));
        first.Metatable = replacement;
        try
        {
            Assert.That(second.Metatable, Is.SameAs(replacement));
            state.Environment["obj"] = obj;
            Assert.That(
                (await state.DoStringAsync("return obj.Value"))[0].Read<int>(),
                Is.EqualTo(99)
            );
            second.Metatable = null;
            var restored = first.Metatable!;
            Assert.That(restored, Is.Not.SameAs(original));
            Assert.That(second.Metatable, Is.SameAs(restored));
            Assert.That(
                (await state.DoStringAsync("return obj:Read()"))[0].Read<int>(),
                Is.EqualTo(7)
            );
        }
        finally
        {
            first.Metatable = null;
        }
    }
}
