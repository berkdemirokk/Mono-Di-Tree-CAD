using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfInsertPrecisionTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("-0.000", 0)]
    [InlineData("90.000", 1)]
    [InlineData("-90", 3)]
    [InlineData("-450", 3)]
    [InlineData("2.70e2", 3)]
    [InlineData("9e256", 0)]
    public void ExactDecimalAnglesProduceNormalizedQuarterTurns(string degrees, int expected)
    {
        Assert.True(DxfInsertPrecision.TryGetQuarterTurns(degrees, out var turns));
        Assert.Equal(expected, turns);
    }

    [Theory]
    [InlineData("89.999999999999999")]
    [InlineData("90.000000000000001")]
    [InlineData("1e-256")]
    [InlineData("45")]
    public void NonCardinalAnglesAreNotRoundedIntoQuarterTurns(string degrees)
    {
        Assert.False(DxfInsertPrecision.TryGetQuarterTurns(degrees, out var turns));
        Assert.Equal(0, turns);
    }

    [Theory]
    [InlineData("")]
    [InlineData("90e")]
    [InlineData("1e257")]
    [InlineData("NaN")]
    [InlineData("90,0")]
    public void MalformedOrOutOfBudgetAnglesAreRejected(string degrees)
    {
        Assert.Throws<InvalidDataException>(() => DxfInsertPrecision.TryGetQuarterTurns(degrees, out _));
    }

    [Fact]
    public void QuarterTurnParsingHonorsCancellation()
    {
        using var source = new CancellationTokenSource(); source.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => DxfInsertPrecision.TryGetQuarterTurns("90", out _, source.Token));
    }

    private static DxfPrecisionTransform T(string bx, string by, string ix, string iy, string sx, string sy, int turns) =>
        new(bx, by, ix, iy, sx, sy, turns);

    [Fact]
    public void ComparesEntireNestedPlacementAndUnitsToExactReference()
    {
        DxfPrecisionTransform[] chain = [T("1", "2", "5", "10", "2", "1", 1), T("0", "0", "100", "200", "-1", "2", 0)];
        // (4,6) -> (1,16) -> (99,232) -> (990,2320) mm.
        Assert.True(DxfInsertPrecision.IsWithinTolerance("4", "6", chain, "10", 990, 2320, 0));
        Assert.False(DxfInsertPrecision.IsWithinTolerance("4", "6", chain, "10", 990, 2319.99, 0.001));
    }

    [Fact]
    public void DetectsLostSourceDifferenceAndLostInsertionEvenWithoutPointCollision()
    {
        DxfPrecisionTransform[] first = [T("10000000000000002", "0", "0", "0", "1", "1", 0)];
        Assert.False(DxfInsertPrecision.IsWithinTolerance("10000000000000001", "0", first, "1", -2, 0, 0.1));
        DxfPrecisionTransform[] second = [T("0", "0", "10000000000000001", "0", "1", "1", 0)];
        Assert.False(DxfInsertPrecision.IsWithinTolerance("0", "0", second, "1", 10000000000000000d, 0, 0.1));
    }

    [Fact]
    public void DecimalErrorIsJudgedAgainstExplicitMillimeterTolerance()
    {
        Assert.False(DxfInsertPrecision.IsWithinTolerance("0.1", "-0.1", [], "1", 0.1, -0.1, 0));
        Assert.True(DxfInsertPrecision.IsWithinTolerance("0.1", "-0.1", [], "1", 0.1, -0.1, 1e-15));
        Assert.True(DxfInsertPrecision.IsWithinTolerance("1", "0", [], "1", 0, 0, 1));
        Assert.False(DxfInsertPrecision.IsWithinTolerance("1", "0", [], "1", 0, 0, 0.5));
    }

    [Fact]
    public void NegativeAndLargeQuarterTurnCountsHaveCorrectOrientation()
    {
        Assert.True(DxfInsertPrecision.IsWithinTolerance("1", "2", [T("0", "0", "0", "0", "1", "1", -1)], "1", 2, -1, 0));
        Assert.True(DxfInsertPrecision.IsWithinTolerance("1", "2", [T("0", "0", "0", "0", "1", "1", 5)], "1", -2, 1, 0));
    }

    [Fact]
    public void BoundsInputAndRejectsInvalidScalesAndCancellation()
    {
        Assert.Throws<InvalidDataException>(() => DxfInsertPrecision.IsWithinTolerance("1e257", "0", [], "1", 0, 0, 0));
        Assert.Throws<InvalidDataException>(() => DxfInsertPrecision.IsWithinTolerance("1", "0", [T("0", "0", "0", "0", "0", "1", 0)], "1", 0, 0, 0));
        Assert.Throws<InvalidDataException>(() => DxfInsertPrecision.IsWithinTolerance("1", "0", [], "0", 0, 0, 0));
        Assert.Throws<InvalidDataException>(() => DxfInsertPrecision.IsWithinTolerance("1", "0", Enumerable.Repeat(T("0", "0", "0", "0", "1", "1", 0), 65).ToArray(), "1", 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DxfInsertPrecision.IsWithinTolerance("1", "0", [], "1", 0, 0, double.NaN));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => DxfInsertPrecision.IsWithinTolerance("1", "0", [], "1", 0, 0, 0, cts.Token));
    }
}
