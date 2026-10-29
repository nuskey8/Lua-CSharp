using Lua.Standard;

namespace Lua.Tests;

[LuaObject]
public partial class ValueConversionObject
{
    [LuaMember]
    public static int Int32(int value) => value;

    [LuaMember]
    public static long Int64(long value) => value;

    [LuaMember]
    public static uint UInt32(uint value) => value;

    [LuaMember]
    public static ulong UInt64(ulong value) => value;

    [LuaMember]
    public static string ObjectType(object value) => value.GetType().Name;

    [LuaMember]
    public static string StringEcho(string value) => value;
}

public class ValueConversionTests
{
    static void AssertRead<T>(double number, T expected)
    {
        LuaValue value = number;
        Assert.That(value.TryRead<T>(out var actual), Is.True);
        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(value.Read<T>(), Is.EqualTo(expected));
    }

    static void AssertRejected<T>(double number)
    {
        LuaValue value = number;
        Assert.That(value.TryRead<T>(out var actual), Is.False);
        Assert.That(actual, Is.EqualTo(default(T)));
        Assert.Throws<InvalidOperationException>(() => value.Read<T>());
    }

    [Test]
    public void IntegerReadsAcceptRepresentableBounds()
    {
        AssertRead<int>(int.MinValue, int.MinValue);
        AssertRead<int>(int.MaxValue, int.MaxValue);
        AssertRead<long>(long.MinValue, long.MinValue);
        AssertRead<long>(9223372036854774784d, 9223372036854774784L);
        AssertRead<uint>(uint.MaxValue, uint.MaxValue);
        AssertRead<ulong>(18446744073709549568d, 18446744073709549568UL);
        AssertRead<int>(-0d, 0);
        AssertRead<long>(-0d, 0L);
        AssertRead<uint>(-0d, 0U);
        AssertRead<ulong>(-0d, 0UL);
    }

    [Test]
    public void IntegerReadsRejectAdjacentOutOfRangeValues()
    {
        AssertRejected<int>((double)int.MinValue - 1);
        AssertRejected<int>((double)int.MaxValue + 1);
        AssertRejected<long>(Math.BitDecrement((double)long.MinValue));
        AssertRejected<long>(9223372036854775808d);
        AssertRejected<uint>(-1);
        AssertRejected<uint>((double)uint.MaxValue + 1);
        AssertRejected<ulong>(-1);
        AssertRejected<ulong>(18446744073709551616d);
    }

    [TestCase(double.NaN)]
    [TestCase(double.NegativeInfinity)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(1.25)]
    [TestCase(1e30)]
    public void InvalidIntegerReadsReturnFalseWithoutThrowing(double number)
    {
        AssertRejected<int>(number);
        AssertRejected<long>(number);
        AssertRejected<uint>(number);
        AssertRejected<ulong>(number);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BooleansReadAsObjectsPreserveTheirTypeAndRoundTrip(bool boolean)
    {
        LuaValue value = boolean;
        Assert.That(value.TryRead<object>(out var boxed), Is.True);
        Assert.That(boxed, Is.TypeOf<bool>().And.EqualTo(boolean));
        Assert.That(value.Read<object>(), Is.TypeOf<bool>().And.EqualTo(boolean));
        Assert.That(LuaValue.FromObject(boxed), Is.EqualTo(value));
    }

    [TestCase("Int32", 2147483648d)]
    [TestCase("Int64", 9223372036854775808d)]
    [TestCase("UInt32", -1d)]
    [TestCase("UInt64", 18446744073709551616d)]
    public async Task GeneratedIntegerArgumentsRejectOverflowAsLuaErrors(
        string method,
        double number
    )
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["target"] = new ValueConversionObject();
        state.Environment["number"] = number;
        var result = await state.DoStringAsync($"return pcall(target.{method},number)");
        Assert.That(result[0], Is.EqualTo(new LuaValue(false)));
        Assert.That(
            result[1].Read<string>(),
            Does.Contain("bad argument #1").And.Contain("integer representation")
        );
        Assert.That(
            (await state.DoStringAsync($"return target.{method}(42)"))[0],
            Is.EqualTo(new LuaValue(42))
        );
        Assert.That(state.Stack.Count, Is.Zero);
    }

    [Test]
    public async Task GeneratedObjectArgumentsReceiveBooleansAndStringArgumentsStayStrict()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["target"] = new ValueConversionObject();
        var result = await state.DoStringAsync(
            "return target.ObjectType(true),target.ObjectType(false),target.ObjectType(12),pcall(target.StringEcho,123)"
        );
        Assert.That(result.Take(3), Is.EqualTo(new LuaValue[] { "Boolean", "Boolean", "Double" }));
        Assert.That(result[3], Is.EqualTo(new LuaValue(false)));
        Assert.That(new LuaValue(123).TryRead<string>(out _), Is.False);
    }
}
