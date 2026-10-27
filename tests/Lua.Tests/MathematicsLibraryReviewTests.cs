using Lua.Standard;

namespace Lua.Tests;

public class MathematicsLibraryReviewTests
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
}

public class FrexpReconstructionTests
{
    [Test]
    public void FiniteValuesNormalizeAndReconstructBitExactly()
    {
        var random = new Random(0x52);
        for (var i = 0; i < 4096; i++)
        {
            var bits = random.NextInt64(long.MinValue, long.MaxValue);
            if ((i & 1) == 0)
                bits &= unchecked((long)0x800fffffffffffff); // Include subnormals.
            var input = BitConverter.Int64BitsToDouble(bits);
            if (!double.IsFinite(input))
                continue;
            var (mantissa, exponent) = MathEx.Frexp(input);
            if (input == 0)
                Assert.That(exponent, Is.Zero);
            else
                Assert.That(Math.Abs(mantissa), Is.GreaterThanOrEqualTo(0.5).And.LessThan(1));
            Assert.That(
                BitConverter.DoubleToInt64Bits(Math.ScaleB(mantissa, exponent)),
                Is.EqualTo(bits)
            );
        }
    }
}
