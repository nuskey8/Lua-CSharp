using Lua.Standard;

namespace Lua.Tests;

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
