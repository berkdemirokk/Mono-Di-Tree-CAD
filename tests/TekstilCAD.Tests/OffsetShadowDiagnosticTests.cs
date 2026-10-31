using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class OffsetShadowDiagnosticTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmoothLobedCandidateRetainsEdgeOrder(bool reverse)
    {
        var points = Enumerable.Range(0, 512).Select(i =>
        {
            var a = 2 * Math.PI * i / 512; var r = 1000 + 200 * Math.Cos(3 * a);
            return new Point2(r * Math.Cos(a), r * Math.Sin(a));
        }).ToList();
        if (reverse) points.Reverse();
        var before = points.ToArray();
        var result = OffsetShadowDiagnostic.Analyze(points, 1);
        Assert.True(result.CandidateConstructed);
        Assert.True(result.CandidateSimple);
        Assert.True(result.EdgeOrderPreserved);
        Assert.InRange(result.MaximumMiterRatio, 1, 2);
        Assert.Equal(before, points);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NarrowRecessIsFlagged(bool reverse)
    {
        List<Point2> points = [new(0,0),new(100,0),new(100,100),new(52,100),new(52,20),new(48,20),new(48,100),new(0,100)];
        if (reverse) points.Reverse();
        var result = OffsetShadowDiagnostic.Analyze(points, 5);
        Assert.True(result.CandidateConstructed);
        Assert.True(!result.CandidateSimple || !result.EdgeOrderPreserved);
    }
}
