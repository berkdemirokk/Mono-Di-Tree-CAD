using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class SeamConvexPrecisionTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public void PositiveAllowanceThatRoundsBackToSourceIsRejected(bool reverse)
    {
        const double origin = 90_000_000;
        var source = new PatternPiece { Points = [new(origin, origin), new(origin + 100, origin),
            new(origin + 100, origin + 100), new(origin, origin + 100)] };
        if (reverse) source.Points.Reverse();
        var before = source.Points.ToArray();
        var error = Assert.Throws<ArgumentException>(() => PatternTools.OffsetConvex(source, 0.000000001));
        Assert.Contains("temsil", error.Message);
        Assert.Equal(before, source.Points);
        var result = PatternTools.OffsetConvex(source, 0.0001220703125);
        Assert.NotEqual(before[0], result.Points[0]);
        Assert.Equal(before, source.Points);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void PositiveAllowanceLostOnOnlyOneAxisIsRejected(bool reverse)
    {
        const double x = 90_000_000;
        var source = new PatternPiece { Points = [new(x, 0), new(x + 100, 0),
            new(x + 100, 100), new(x, 100)] };
        if (reverse) source.Points.Reverse();
        var before = source.Points.ToArray();
        // Y can move by this amount, while either X coordinate rounds unchanged.
        var error = Assert.Throws<ArgumentException>(() => PatternTools.OffsetConvex(source, 0.000000001));
        Assert.Contains("temsil", error.Message);
        Assert.Equal(before, source.Points);
        var result = PatternTools.OffsetConvex(source, 0.0001220703125);
        Assert.True(result.Points.Min(p => p.X) < before.Min(p => p.X));
        Assert.True(result.Points.Min(p => p.Y) < before.Min(p => p.Y));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void PositiveOutwardMovementWithBadlyRoundedDistanceIsRejected(bool reverse)
    {
        const double x = 90_000_000;
        var source = new PatternPiece { Points = [new(x, 0), new(x + 100, 0),
            new(x + 100, 100), new(x, 100)] };
        if (reverse) source.Points.Reverse();
        var before = source.Points.ToArray();
        // At this origin 1e-8 rounds outward to one binary64 ULP, roughly
        // 1.49e-8 mm; a nonzero/outward-only test would accept the wrong width.
        var error = Assert.Throws<ArgumentException>(() => PatternTools.OffsetConvex(source, 0.00000001));
        Assert.Contains("temsil", error.Message);
        Assert.Equal(before, source.Points);
    }
}
