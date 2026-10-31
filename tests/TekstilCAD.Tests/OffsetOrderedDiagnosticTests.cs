using Clipper2Lib;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

public class OffsetOrderedDiagnosticTests
{
    private readonly ITestOutputHelper outputLog;
    public OffsetOrderedDiagnosticTests(ITestOutputHelper outputLog) => this.outputLog = outputLog;
    private static List<Point2> Square(double origin = 0) =>
        [new(origin, origin), new(origin + 100, origin), new(origin + 100, origin + 100), new(origin, origin + 100)];
    private static List<Point2> OffsetSquare(double origin = 0) =>
        [new(origin - 1, origin - 1), new(origin + 101, origin - 1), new(origin + 101, origin + 101), new(origin - 1, origin + 101)];

    [Theory]
    [InlineData(0.0, false, 0)]
    [InlineData(0.0, true, 2)]
    [InlineData(50000000.0, false, 3)]
    [InlineData(50000000.0, true, 1)]
    public void ExactMiterSupportsCyclicShiftAndEitherDirection(double origin, bool reverse, int shift)
    {
        var source = Square(origin); var target = OffsetSquare(origin);
        if (reverse) target.Reverse();
        target = target.Skip(shift).Concat(target.Take(shift)).ToList();
        var sourceBefore = source.ToArray(); var targetBefore = target.ToArray();
        var result = OffsetOrderedDiagnostic.Compare(source, 1, target, .0001);
        Assert.True(result.CandidateConstructed, result.Reason);
        Assert.True(result.OrderedMatch, result.Reason);
        Assert.False(result.Ambiguous);
        Assert.Equal(sourceBefore, source); Assert.Equal(targetBefore, target);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DenseSmoothPolygonMatchesIndependentAnalyticMiter(bool reverse)
    {
        const int count = 512; const double radius = 1000; const double allowance = 1;
        var source = Enumerable.Range(0, count).Select(i =>
        {
            var angle = 2 * Math.PI * i / count;
            return new Point2(radius * Math.Cos(angle), radius * Math.Sin(angle));
        }).ToList();
        var expanded = radius + allowance / Math.Cos(Math.PI / count);
        var target = Enumerable.Range(0, count).Select(i =>
        {
            var angle = 2 * Math.PI * i / count;
            return new Point2(expanded * Math.Cos(angle), expanded * Math.Sin(angle));
        }).ToList();
        if (reverse) { source.Reverse(); target.Reverse(); }
        var result = OffsetOrderedDiagnostic.Compare(source, allowance, target, .00001);
        Assert.True(result.CandidateConstructed, result.Reason);
        Assert.True(result.OrderedMatch, result.Reason);
    }

    [Fact]
    public void SplitEquivalentEdgePreservesOrderedMatch()
    {
        var target = OffsetSquare(); target.Insert(1, new Point2(50, -1));
        var result = OffsetOrderedDiagnostic.Compare(Square(), 1, target, .001);
        Assert.True(result.CandidateConstructed, result.Reason);
        Assert.True(result.OrderedMatch, result.Reason);
        Assert.False(result.Ambiguous);
    }

    [Fact]
    public void NearbyBacktrackingOnOneEdgeIsNotAnOrderedMatch()
    {
        // Both small deviations are inside the corridor, but the middle segment
        // reverses along the edge. Proximity alone cannot establish correspondence.
        List<Point2> target = [new(-1,-1), new(101,-1), new(101,101),
            new(50.001,100.999), new(50.002,100.998), new(-1,101)];
        Geometry.ValidateContour(target);
        var result = OffsetOrderedDiagnostic.Compare(Square(), 1, target, .01);
        Assert.False(result.OrderedMatch);
    }

    [Fact]
    public void MissingNotchIsNeverReportedAsOrderedMatch()
    {
        List<Point2> source = [new(0,0), new(100,0), new(100,100), new(52,100), new(52,20), new(48,20), new(48,100), new(0,100)];
        var result = OffsetOrderedDiagnostic.Compare(source, 5,
            [new(-5,-5), new(105,-5), new(105,105), new(-5,105)], .001);
        Assert.False(result.OrderedMatch);
    }

    [Fact]
    public void AlteredCornerOutsideCorridorCannotMatch()
    {
        var target = OffsetSquare(); target[2] = new Point2(100.99, 101);
        var result = OffsetOrderedDiagnostic.Compare(Square(), 1, target, .001);
        Assert.True(result.CandidateConstructed, result.Reason);
        Assert.False(result.OrderedMatch);
    }

    [Fact]
    public void BroadCorridorCannotCertifyEdgesShorterThanItsDiameter()
    {
        List<Point2> source = [new(0,0), new(.1,0), new(.1,.1), new(0,.1)];
        List<Point2> target = [new(-.01,-.01), new(.11,-.01), new(.11,.11), new(-.01,.11)];
        var result = OffsetOrderedDiagnostic.Compare(source, .01, target, .5);
        Assert.True(result.CandidateConstructed, result.Reason);
        Assert.False(result.OrderedMatch);
        Assert.NotNull(result.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionGridLobedContourProducesExplicitDiagnostic(bool reverse)
    {
        const double scale = 1_000_000;
        var source = Enumerable.Range(0, 512).Select(i =>
        {
            var a = Math.Tau * i / 512; var r = 1000 + 200 * Math.Cos(3 * a);
            return new Point2(Math.Round(r * Math.Cos(a) * scale, MidpointRounding.AwayFromZero) / scale,
                Math.Round(r * Math.Sin(a) * scale, MidpointRounding.AwayFromZero) / scale);
        }).ToList();
        if (reverse) source.Reverse();
        var path = new Path64(source.Select(p => new Point64(
            (long)Math.Round(p.X * scale, MidpointRounding.AwayFromZero),
            (long)Math.Round(p.Y * scale, MidpointRounding.AwayFromZero))));
        var output = Assert.Single(Clipper.InflatePaths(new Paths64 { path }, scale,
            JoinType.Miter, EndType.Polygon, 2.0))
            .Select(p => new Point2(p.X / scale, p.Y / scale)).ToList();
        Assert.Equal(515, output.Count);
        var result = OffsetOrderedDiagnostic.Compare(source, 1, output, .00001);
        outputLog.WriteLine($"reverse={reverse}; constructed={result.CandidateConstructed}; ordered={result.OrderedMatch}; ambiguous={result.Ambiguous}; maxDistanceMm={result.MaximumVertexDistanceMm:G17}; reason={result.Reason ?? "(none)"}");
        Assert.True(result.CandidateConstructed, result.Reason);
        Assert.Equal(512, result.CandidateVertices);
        Assert.Equal(515, result.OutputVertices);
        Assert.True(double.IsFinite(result.MaximumVertexDistanceMm));
    }

    [Fact]
    public void InvalidCorridorIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OffsetOrderedDiagnostic.Compare(Square(), 1, OffsetSquare(), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => OffsetOrderedDiagnostic.Compare(Square(), 1, OffsetSquare(), double.NaN));
    }
}
