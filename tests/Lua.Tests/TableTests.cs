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
}