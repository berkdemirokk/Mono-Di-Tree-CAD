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
        var result = PatternTools.OffsetConvex(source, 0.0001);
        Assert.NotEqual(before[0], result.Points[0]);
        Assert.Equal(before, source.Points);
    }
}
