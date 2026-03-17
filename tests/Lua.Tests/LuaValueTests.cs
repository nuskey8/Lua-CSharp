namespace Lua.Tests;

public class LuaValueTests
{
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
