using Lua.Standard;

namespace Lua.Tests;

public class TableTests
{
    [Test]
    public void Test_Indexer()
    {
        var table = new LuaTable();
        table[1] = "foo";
        table["bar"] = 2;
        table[true] = "baz";

        Assert.That(table[1], Is.EqualTo(new LuaValue("foo")));
        Assert.That(table["bar"], Is.EqualTo(new LuaValue(2)));
        Assert.That(table[true], Is.EqualTo(new LuaValue("baz")));
    }

    [Test]
    public void Test_EnsureCapacity()
    {
        var table = new LuaTable(2, 2);
        table[32] = 10; // hash part

        for (var i = 1; i <= 31; i++)
        {
            table[i] = 10;
        }

        Assert.That(table[32], Is.EqualTo(new LuaValue(10)));
    }

    [Test]
    public void Test_RemoveAt()
    {
        var table = new LuaTable();
        table[1] = 1;
        table[2] = 2;
        table[3] = 3;

        var value = table.RemoveAt(2);
        Assert.That(value, Is.EqualTo(new LuaValue(2)));
        Assert.That(table[2], Is.EqualTo(new LuaValue(3)));
    }

    [Test]
    public void Test_TableResize()
    {
        var table = new LuaTable();
        var i = 1;
        var count = 10000;
        while (count > 0)
        {
            var key = i;
            table[key] = key;
            table[(key * 2) - (key / 2)] = key;
            i += key;
            count--;
        }

        table[1] = 0;
        table[int.MaxValue - 1] = 0;
        Assert.That(table[1], Is.EqualTo(new LuaValue(0)));
        Assert.That(table[int.MaxValue - 1], Is.EqualTo(new LuaValue(0)));
    }

    private const int LargePowerOfTwo = 1 << 13;

    [Test]
    public void Test_TableContains_LargeIntegerKey()
    {
        var table = new LuaTable();
        table[LargePowerOfTwo] = 1;
        Assert.That(table.ContainsKey(LargePowerOfTwo));
    }

    [Test]
    public void Test_TableInsert_IndexOutOfRange()
    {
        var table = new LuaTable();
        table[1] = 0;
        Assert.Throws<IndexOutOfRangeException>(() => table.Insert(3, 0));
    }

    [Test]
    public void Test_Indexer_AddKeysInReverseOrder()
    {
        var table = new LuaTable();
        for (var i = LargePowerOfTwo; i > 0; i--)
        {
            table[i] = 0;
        }
        Assert.That(table.ArrayLength, Is.EqualTo(LargePowerOfTwo));
    }

    [Test]
    public void Test_TableInsert_ResizeArrayWhenReplacingTrailingNil()
    {
        var table = new LuaTable();
        table[LargePowerOfTwo + 1] = 0;
        for (var i = 1; i < LargePowerOfTwo; i++)
        {
            table.Insert(1, 0);
        }
        table.Insert(LargePowerOfTwo, LargePowerOfTwo);
        Assert.That(table.ArrayLength, Is.EqualTo(LargePowerOfTwo + 1));
    }

    [Test]
    public void Test_TableInsert_ResizeArrayWhenInsertShiftsANonNilValueInPlaceOfTrailingNil()
    {
        var table = new LuaTable();
        table[LargePowerOfTwo + 1] = 0;
        for (var i = 1; i <= LargePowerOfTwo; i++)
        {
            table.Insert(1, 0);
        }
        Assert.That(table.ArrayLength, Is.EqualTo(LargePowerOfTwo + 1));
    }

    [Test]
    public void Test_TableInsert_EnsureValuesAfterFirstNilAreNotShifted()
    {
        var table = new LuaTable();
        table[1] = 1;
        table[3] = 3;
        table.Insert(2, 2);
        Assert.That(table[3].Read<int>(), Is.EqualTo(3));
    }

    [Test]
    public void Test_TableInsert_EnsureValuesAfterFirstNilAtTheEndOfTheArrayAreNotShifted()
    {
        var table = new LuaTable();
        table[32] = 32;
        for (var i = 16; i > 0; i--)
        {
            table[i] = 0;
        }
        table.Insert(1, 0);
        Assert.That(table[32].Read<int>(), Is.EqualTo(32));
    }

    [Test]
    public void Test_ArrayLength_AfterMutations()
    {
        var table = new LuaTable();
        for (var i = 1; i <= 4; i++)
        {
            table[i] = i;
        }

        Assert.That(table.ArrayLength, Is.EqualTo(4));
        table[2] = LuaValue.Nil;
        Assert.That(table.ArrayLength, Is.EqualTo(1));
        table[2] = 2;
        Assert.That(table.ArrayLength, Is.EqualTo(4));
        table.RemoveAt(2);
        Assert.That(table.ArrayLength, Is.EqualTo(3));
        table.Insert(2, LuaValue.Nil);
        Assert.That(table.ArrayLength, Is.EqualTo(1));
        table[2] = 2;
        Assert.That(table.ArrayLength, Is.EqualTo(4));
        table.Clear();
        Assert.That(table.ArrayLength, Is.Zero);
        table.Insert(1, 7);
        Assert.That(table.ArrayLength, Is.EqualTo(1));
    }

    [Test]
    public void Test_TableInsert_ConnectsValuesAfterFirstNil()
    {
        var table = new LuaTable();
        table[1] = 1;
        table[3] = 3;
        Assert.That(table.ArrayLength, Is.EqualTo(1));

        table.Insert(2, 2);
        Assert.That(table.ArrayLength, Is.EqualTo(3));
        table.Insert(4, 4);
        Assert.That(table.ArrayLength, Is.EqualTo(4));
    }

    [Test]
    public void Test_ArrayLength_RetainedSpanCanBeModifiedRepeatedly()
    {
        var table = new LuaTable();
        table[1] = 1;
        table[2] = 2;
        Assert.That(table.ArrayLength, Is.EqualTo(2));

        var span = table.GetArraySpan();
        span[0] = LuaValue.Nil;
        Assert.That(table.ArrayLength, Is.Zero);
        span[0] = 1;
        Assert.That(table.ArrayLength, Is.EqualTo(2));
        span[1] = LuaValue.Nil;
        Assert.That(table.ArrayLength, Is.EqualTo(1));
        Assert.Throws<IndexOutOfRangeException>(() => table.Insert(3, 3));
    }

    [Test]
    public void Test_ArrayLength_RetainedMemoryCanBeModifiedAfterClear()
    {
        var table = new LuaTable();
        table[1] = 1;
        Assert.That(table.ArrayLength, Is.EqualTo(1));
        var memory = table.GetArrayMemory();

        table.Clear();
        Assert.That(table.ArrayLength, Is.Zero);
        memory.Span[0] = 1;
        Assert.That(table.ArrayLength, Is.EqualTo(1));
        memory.Span[0] = LuaValue.Nil;
        Assert.That(table.ArrayLength, Is.Zero);
    }

    [Test]
    public void Test_ArrayLength_ResizingDetachesRetainedMemory()
    {
        var table = new LuaTable(2, 0);
        table[1] = 1;
        table[2] = 2;
        var memory = table.GetArrayMemory();
        memory.Span[0] = LuaValue.Nil;
        table[3] = 3;
        Assert.That(table.ArrayLength, Is.Zero);

        memory.Span[0] = 1;
        Assert.That(table.ArrayLength, Is.Zero);
        table[1] = 1;
        Assert.That(table.ArrayLength, Is.EqualTo(3));
        memory.Span[1] = LuaValue.Nil;
        Assert.That(table.ArrayLength, Is.EqualTo(3));
    }

    [Test]
    public async Task Test_ArrayLength_AfterVirtualMachineWrites()
    {
        using var state = LuaState.Create();
        state.OpenBasicLibrary();
        var table = new LuaTable();
        table[1] = 1;
        table[2] = 2;
        Assert.That(table.ArrayLength, Is.EqualTo(2));
        state.Environment["t"] = table;

        var results = await state.DoStringAsync(
            """
            assert(#t == 2)
            t[1] = nil
            assert(#t == 0)
            t[1] = 1
            assert(#t == 2)
            t[2] = nil
            return #t
            """
        );
        Assert.That(results[0].Read<int>(), Is.EqualTo(1));
        Assert.That(table.ArrayLength, Is.EqualTo(1));
    }
}
