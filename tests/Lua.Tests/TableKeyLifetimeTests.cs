using System.Runtime.CompilerServices;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

public class TableKeyLifetimeTests
{
    [TestCase("table", false)]
    [TestCase("function", false)]
    [TestCase("string", false)]
    [TestCase("thread", false)]
    [TestCase("userdata", false)]
    [TestCase("object", false)]
    [TestCase("table", true)]
    [TestCase("function", true)]
    [TestCase("string", true)]
    [TestCase("thread", true)]
    [TestCase("userdata", true)]
    [TestCase("object", true)]
    public void DeletedReferenceKeyIsCollectible(string kind, bool hostApi)
    {
        var table = new LuaTable();
        var reference = DeleteKey(table, kind, hostApi);
        Collect(reference);
        Assert.That(table.HashMapCount, Is.EqualTo(1));
        GC.KeepAlive(table);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference DeleteKey(LuaTable table, string kind, bool hostApi)
    {
        LuaValue key = kind switch
        {
            "table" => new LuaTable(),
            "function" => new LuaFunction((context, _) => new(context.Return())),
            "string" => new string('k', 1024),
            "thread" => LuaState.Create(),
            "userdata" => new LuaValue(new KeyUserData()),
            _ => LuaValue.FromObject(new object()),
        };
        table["anchor"] = 1;
        table[key] = 42;
        if (hostApi)
        {
            using var state = LuaState.Create();
            state.SetTableAsync(table, key, LuaValue.Nil).GetAwaiter().GetResult();
        }
        else
            table[key] = LuaValue.Nil;
        if (kind == "thread")
            key.Read<LuaState>().Dispose();
        return new WeakReference(key.UnsafeRead<object>());
    }

    [TestCase("t[k]=nil")]
    [TestCase("rawset(t,k,nil)")]
    [TestCase("local proxy=setmetatable({}, {__newindex=t}); proxy[k]=nil")]
    public void LuaDeletionReleasesKey(string deletion)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var reference = DeleteLuaKey(state, deletion);
        Collect(reference);
        GC.KeepAlive(state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference DeleteLuaKey(LuaState state, string deletion)
    {
        var key = new LuaTable();
        state.Environment["k"] = key;
        state
            .DoStringAsync("t={anchor=1}; t[k]=42; " + deletion + "; k=nil")
            .AsTask()
            .GetAwaiter()
            .GetResult();
        return new WeakReference(key);
    }

    [Test]
    public async Task NextContinuesAfterDeletingAndCollectingCurrentKeys()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["collect"] = new LuaFunction(
            (context, _) =>
            {
                var other = new LuaTable();
                other["anchor"] = 1;
                for (var i = 0; i < 8; i++)
                {
                    var key = new LuaTable();
                    other[key] = 1;
                    other[key] = LuaValue.Nil;
                }
                other.Clear();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                return new(context.Return());
            }
        );
        var result = await state.DoStringAsync(
            """
            local t={[-1]=1,[false]=2,[string.rep('k',100)]=3,[{}]=4,[function() end]=5}
            local k=next(t)
            local count=0
            while k~=nil do
                t[k]=nil
                collect()
                k=next(t,k)
                count=count+1
            end
            return count,next(t)
            """
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { 5, LuaValue.Nil }));
    }

    [Test]
    public void DeletedEntriesAreReclaimedDuringInsertion()
    {
        var table = new LuaTable(0, 8);
        table["anchor"] = 1;
        for (var i = 0; i < 1000; i++)
        {
            var key = new LuaTable();
            table[key] = i;
            table[key] = LuaValue.Nil;
        }
        table["live"] = 42;
        Assert.That(table.Dictionary.Count, Is.LessThanOrEqualTo(8));
        Assert.That(table.HashMapCount, Is.EqualTo(2));
        Assert.That(table["live"], Is.EqualTo(new LuaValue(42)));
    }

    [Test]
    public void AssigningNilToMissingKeysDoesNotCreateEntries()
    {
        var table = new LuaTable(0, 0);
        for (var i = 0; i < 32; i++)
            table[new LuaTable()] = LuaValue.Nil;
        Assert.That(table.Dictionary.Count, Is.Zero);
    }

    [Test]
    public void CompactionDoesNotRetainCopiesOfMovedValues()
    {
        var table = new LuaTable(0, 8);
        Collect(CompactAndDeleteValue(table));
        Assert.That(table["trigger"], Is.EqualTo(new LuaValue(1)));
        GC.KeepAlive(table);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference CompactAndDeleteValue(LuaTable table)
    {
        table["anchor"] = 1;
        for (var i = 0; i < 6; i++)
        {
            var key = new LuaTable();
            table[key] = i;
            table[key] = LuaValue.Nil;
        }
        var payload = new object();
        table["live"] = LuaValue.FromObject(payload);
        table["trigger"] = 1;
        table["live"] = LuaValue.Nil;
        return new WeakReference(payload);
    }

    [Test]
    public async Task ReinsertingDeletedKeysPreservesValuesAndCounts()
    {
        using var state = LuaState.Create();
        var table = new LuaTable(0, 8);
        state.Environment["t"] = table;
        await state.DoStringAsync(
            """
            local keys={-1,false,'name',{}}
            for i=1,4 do
                local k=keys[i]
                t[k]=i
                t[k]=nil
                t[k]=nil
                t[k]=i+10
            end
            """
        );
        Assert.That(table.HashMapCount, Is.EqualTo(4));
        Assert.That(
            table.Select(pair => pair.Value.Read<int>()).Order(),
            Is.EqualTo(new[] { 11, 12, 13, 14 })
        );
    }

    [Test]
    public async Task HostDeletionPreservesCountsReinsertionAndIteration()
    {
        using var state = LuaState.Create();
        var table = new LuaTable(0, 8);
        LuaValue[] keys = [-1, false, "name", new LuaTable()];
        foreach (var key in keys)
        {
            await state.SetTableAsync(table, key, 42);
            await state.SetTableAsync(table, key, LuaValue.Nil);
            await state.SetTableAsync(table, key, 99);
        }
        Assert.That(table.HashMapCount, Is.EqualTo(keys.Length));
        LuaValue previous = LuaValue.Nil;
        var removed = 0;
        while (table.TryGetNext(previous, out var pair))
        {
            previous = pair.Key;
            Assert.That(pair.Value, Is.EqualTo(new LuaValue(99)));
            await state.SetTableAsync(table, previous, LuaValue.Nil);
            removed++;
        }
        Assert.That(removed, Is.EqualTo(keys.Length));
        Assert.That(table.HashMapCount, Is.Zero);
    }

    static void Collect(WeakReference reference)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.That(reference.IsAlive, Is.False);
    }

    sealed class KeyUserData : ILuaUserData
    {
        public LuaTable? Metatable { get; set; }
    }
}
