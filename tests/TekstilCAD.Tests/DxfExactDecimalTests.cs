using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfExactDecimalTests
{
    [Fact]
    public void RejectsLostUnitEvenWhenParsedCoordinatesAreDifferent()
    {
        Assert.False(DxfExactDecimal.IsDifferenceWithinTolerance("10000000000000002", "10000000000000001", 0.1));
        Assert.True(DxfExactDecimal.IsDifferenceWithinTolerance("10000000000000002", "10000000000000001", 1));
    }

    [Fact]
    public void AllowsOrdinaryDecimalRoundingWithinExplicitTolerance()
    {
        Assert.True(DxfExactDecimal.IsDifferenceWithinTolerance("0.3", "0.1", 1e-15));
        Assert.False(DxfExactDecimal.IsDifferenceWithinTolerance("0.3", "0.1", 0));
        Assert.True(DxfExactDecimal.IsDifferenceWithinTolerance("2.5", "1.25", 0));
    }

    [Fact]
    public void HandlesNegativeScientificValuesAndTinyUnderflow()
    {
        Assert.True(DxfExactDecimal.IsDifferenceWithinTolerance("-2.5e1", "-2.4e1", 0));
        Assert.False(DxfExactDecimal.IsDifferenceWithinTolerance("1e-400", "0", 0));
        Assert.True(DxfExactDecimal.IsDifferenceWithinTolerance("1e-400", "0", double.Epsilon));
    }

    [Fact]
    public void ToleranceIsAbsoluteAndEqualityAtBoundaryIsAllowed()
    {
        Assert.False(DxfExactDecimal.IsDifferenceWithinTolerance("10000000000000001", "10000000000000000", 0.5));
        Assert.True(DxfExactDecimal.IsDifferenceWithinTolerance("10000000000000001", "10000000000000000", 1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1e")]
    [InlineData("1e1025")]
    [InlineData("1,2")]
    [InlineData("NaN")]
    public void RejectsInvalidOrUnboundedTokens(string value)
        => Assert.Throws<InvalidDataException>(() => DxfExactDecimal.IsDifferenceWithinTolerance(value, "0", 1));

    [Fact]
    public void RejectsInvalidToleranceAndObservesCancellation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DxfExactDecimal.IsDifferenceWithinTolerance("1", "0", -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DxfExactDecimal.IsDifferenceWithinTolerance("1", "0", double.NaN));
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => DxfExactDecimal.IsDifferenceWithinTolerance("1", "0", 0, source.Token));
    }
}
