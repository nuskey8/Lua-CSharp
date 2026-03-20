namespace Lua.Tests;

public class LuaValueTests
{
    [Test]
    public void TryRead_LuaValue_ReturnsOriginalValue()
    {
        LuaValue value = "hello";

        var success = value.TryRead<LuaValue>(out var result);

        Assert.That(success, Is.True);
        Assert.That(result, Is.EqualTo(value));
    }

    [Test]
    public void TryRead_LuaValue_SucceedsForNil()
    {
        var success = LuaValue.Nil.TryRead<LuaValue>(out var result);

        Assert.That(success, Is.True);
        Assert.That(result, Is.EqualTo(LuaValue.Nil));
    }

    [Test]
    public void Read_LuaValue_ReturnsOriginalValue()
    {
        LuaValue value = 42;

        var result = value.Read<LuaValue>();

        Assert.That(result, Is.EqualTo(value));
    }

    [Test]
    public void TryRead_LuaTable_ReturnsOriginalTable()
    {
        var table = new LuaTable();
        LuaValue value = table;

        var success = value.TryRead<LuaTable>(out var result);

        Assert.That(success, Is.True);
        Assert.That(result, Is.SameAs(table));
    }

    [Test]
    public void FromObject_ConvertsUIntToNumber()
    {
        object value = (uint)42;

        var result = LuaValue.FromObject(value);

        Assert.That(result.Type, Is.EqualTo(LuaValueType.Number));
        Assert.That(result.Read<uint>(), Is.EqualTo((uint)42));
    }

    [Test]
    public void FromObject_ConvertsULongToNumber()
    {
        object value = (ulong)42;

        var result = LuaValue.FromObject(value);

        Assert.That(result.Type, Is.EqualTo(LuaValueType.Number));
        Assert.That(result.Read<ulong>(), Is.EqualTo((ulong)42));
    }

    [Test]
    public void TryGetLuaValueType_ReturnsNumberForUnsignedIntegers()
    {
        Assert.That(LuaValue.TryGetLuaValueType(typeof(uint), out var uintType), Is.True);
        Assert.That(uintType, Is.EqualTo(LuaValueType.Number));

        Assert.That(LuaValue.TryGetLuaValueType(typeof(ulong), out var ulongType), Is.True);
        Assert.That(ulongType, Is.EqualTo(LuaValueType.Number));
    }
}
