using System.Numerics;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfTrigIntervalTests
{
    private static DxfTrigFraction F(long n, long d = 1) => DxfTrigFraction.Create(n, d);
    private static DxfTrigFraction Decimal(string digits) => DxfTrigFraction.Create(BigInteger.Parse(digits), BigInteger.Pow(10, digits.Length));
    private static void Contains(DxfTrigFraction lower, DxfTrigFraction upper, DxfTrigFraction value)
    {
        Assert.True(lower.Compare(value) <= 0);
        Assert.True(upper.Compare(value) >= 0);
        Assert.True(lower.Compare(upper) <= 0);
    }

    [Theory]
    [InlineData("0", 0, 1)]
    [InlineData("90", 1, 0)]
    [InlineData("180", 0, -1)]
    [InlineData("270", -1, 0)]
    [InlineData("-90", -1, 0)]
    [InlineData("450", 1, 0)]
    public void CardinalAnglesAreExact(string input, int sin, int cos)
    {
        var bounds = DxfTrigInterval.Compute(input);
        Assert.Equal(F(sin), bounds.SinLower);
        Assert.Equal(F(sin), bounds.SinUpper);
        Assert.Equal(F(cos), bounds.CosLower);
        Assert.Equal(F(cos), bounds.CosUpper);
    }

    [Fact]
    public void ThirtyDegreesEnclosesIndependentReference()
    {
        var bounds = DxfTrigInterval.Compute("30");
        Contains(bounds.SinLower, bounds.SinUpper, F(1, 2));
        // Independent 50-digit decimal reference for sqrt(3)/2.
        Contains(bounds.CosLower, bounds.CosUpper, Decimal("86602540378443864676372317075293618347140262690519"));
    }

    [Theory]
    [InlineData("45")]
    [InlineData("405")]
    public void FortyFiveDegreesEnclosesIndependentReference(string angle)
    {
        var bounds = DxfTrigInterval.Compute(angle);
        // Independent 50-digit decimal reference for sqrt(2)/2.
        var reference = Decimal("70710678118654752440084436210484903928483593768847");
        Contains(bounds.SinLower, bounds.SinUpper, reference);
        Contains(bounds.CosLower, bounds.CosUpper, reference);
    }

    [Fact]
    public void NegativeAngleHasExpectedSigns()
    {
        var positive = DxfTrigInterval.Compute("30");
        var negative = DxfTrigInterval.Compute("-30");
        Assert.Equal(positive.SinLower.Negate(), negative.SinUpper);
        Assert.Equal(positive.SinUpper.Negate(), negative.SinLower);
        Assert.Equal(positive.CosLower, negative.CosLower);
        Assert.Equal(positive.CosUpper, negative.CosUpper);
    }

    [Theory]
    [InlineData("")]
    [InlineData("NaN")]
    [InlineData("1e129")]
    [InlineData("1e-")]
    public void InvalidOrOverBudgetAngleIsRejected(string input)
        => Assert.Throws<InvalidDataException>(() => DxfTrigInterval.Compute(input));

    [Fact]
    public void CancellationIsObserved()
    {
        using var source = new CancellationTokenSource(); source.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => DxfTrigInterval.Compute("45", source.Token));
    }
}
