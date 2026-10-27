using Lua.Standard;

namespace Lua.Tests;

public class ConversionLibraryReviewTests
{
    static LuaState CreateState()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        return state;
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
