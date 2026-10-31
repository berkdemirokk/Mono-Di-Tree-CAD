using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class SeamExactActualOutputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentSquareReferenceMatchesReturnedBoundaryExactly(bool reverse)
    {
        List<Point2> source = [new(0,0), new(10,0), new(10,10), new(0,10)];
        GridPoint[] reference = [new(-1_000_000,-1_000_000), new(11_000_000,-1_000_000),
            new(11_000_000,11_000_000), new(-1_000_000,11_000_000)];
        if (reverse) { source.Reverse(); Array.Reverse(reference); }
        var actual = PatternTools.Offset(new PatternPiece { Closed = true, Points = source }, 1).Points;
        // Require integral scaled doubles; multiplication rounding means this
        // alone does not prove exact binary-to-rational grid membership.
        var grid = new List<GridPoint>();
        foreach (var p in actual)
        {
            double x = p.X * 1_000_000, y = p.Y * 1_000_000;
            Assert.True(double.IsFinite(x) && double.IsFinite(y));
            Assert.Equal(Math.Round(x), x);
            Assert.Equal(Math.Round(y), y);
            grid.Add(new GridPoint(checked((long)Math.Round(x)), checked((long)Math.Round(y))));
        }
        Assert.True(SeamExactOutputBridge.Matches(reference, grid, 1000, CancellationToken.None));
    }
}

