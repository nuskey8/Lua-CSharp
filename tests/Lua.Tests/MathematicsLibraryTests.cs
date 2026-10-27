using Lua.Standard;

namespace Lua.Tests;

public class MathematicsLibraryTests
{
    static LuaState CreateState()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        return state;
    }

    [TestCase(1, 3.5, 8)]
    [TestCase(1, -3.5, 0.125)]
    [TestCase(8, -1077, double.Epsilon)]
    [TestCase(0, 2000, 0)]
    [TestCase(1, 4294967296d, 1)]
    public async Task LdexpScalesWithoutPrematureUnderflow(
        double mantissa,
        double exponent,
        double expected
    )
    {
        using var state = CreateState();
        var results = await state.CallAsync(
            state.Environment["math"].Read<LuaTable>()["ldexp"],
            [mantissa, exponent]
        );
        Assert.That(results[0].Read<double>(), Is.EqualTo(expected));
    }

    [TestCase(double.Epsilon, 0.5, -1073)]
    [TestCase(-double.Epsilon, -0.5, -1073)]
    [TestCase(2.225073858507201e-308, 0.9999999999999998, -1022)]
    [TestCase(2.2250738585072014e-308, 0.5, -1021)]
    [TestCase(double.MaxValue, 0.9999999999999999, 1024)]
    [TestCase(1, 0.5, 1)]
    [TestCase(-3, -0.75, 2)]
    [TestCase(12, 0.75, 4)]
    public async Task FrexpPreservesFiniteNormalization(double input, double mantissa, int exponent)
    {
        using var state = CreateState();
        var math = state.Environment["math"].Read<LuaTable>();
        var values = await state.CallAsync(math["frexp"], [input]);
        Assert.That(values.Length, Is.EqualTo(2));
        Assert.That(values[0].Read<double>(), Is.EqualTo(mantissa));
        Assert.That(values[1].Read<double>(), Is.EqualTo(exponent));
    }

    [Test]
    public async Task AngleConversionsAndFrexpPreserveBothZeroSigns()
    {
        using var state = CreateState();
        var math = state.Environment["math"].Read<LuaTable>();
        foreach (var bits in new[] { 0L, long.MinValue })
        foreach (var name in new[] { "deg", "rad", "frexp" })
        {
            var values = await state.CallAsync(math[name], [BitConverter.Int64BitsToDouble(bits)]);
            Assert.That(
                BitConverter.DoubleToInt64Bits(values[0].Read<double>()),
                Is.EqualTo(bits),
                name
            );
            if (name == "frexp")
                Assert.That(values[1].Read<double>(), Is.Zero);
        }
    }

    [TestCase(-0.0)]
    [TestCase(-1d)]
    [TestCase(-1.5)]
    [TestCase(1e20)]
    [TestCase(-1e20)]
    [TestCase(double.Epsilon)]
    [TestCase(-double.Epsilon)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    [TestCase(double.NaN)]
    public async Task ModfKeepsDoubleRangeAndSignedFractionalZero(double number)
    {
        using var state = CreateState();
        var values = await state.CallAsync(
            state.Environment["math"].Read<LuaTable>()["modf"],
            [number]
        );
        var integral = values[0].Read<double>();
        var fraction = values[1].Read<double>();
        if (double.IsNaN(number))
        {
            Assert.That(double.IsNaN(integral) && double.IsNaN(fraction), Is.True);
            return;
        }
        Assert.That(
            BitConverter.DoubleToInt64Bits(integral),
            Is.EqualTo(BitConverter.DoubleToInt64Bits(Math.Truncate(number)))
        );
        if (double.IsInfinity(number) || number == Math.Truncate(number))
            Assert.That(
                BitConverter.DoubleToInt64Bits(fraction),
                Is.EqualTo(BitConverter.DoubleToInt64Bits(number) & long.MinValue)
            );
        else
            Assert.That(integral + fraction, Is.EqualTo(number));
    }

    [Test]
    public async Task RandomSeedsAreNumericAndRepeatable()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            local function sequence(seed)
                math.randomseed(seed)
                local t={}; for i=1,8 do t[i]=math.random() end
                return table.concat(t,',')
            end
            assert(sequence(1)==sequence(1))
            assert(sequence(1)~=sequence(2))
            assert(sequence('1')==sequence(1))
            assert(sequence(1.5)==sequence(2))
            assert(sequence(1+2^32)==sequence(1))
            """
        );
    }
}
