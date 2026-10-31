using System.Reflection;
using Clipper2Lib;
using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

/// <summary>Investigative evidence only; these checks do not certify manufactured geometry.</summary>
public class SeamOffsetInvestigationTests
{
    [Theory]
    [InlineData(0L, false)]
    [InlineData(50000000000000L, true)]
    public void EnlargingRoundTripToleranceCanHideACompletelyLostRecess(long origin, bool reverse)
    {
        // Units are the production grid: the notch is 16 units deep, less than
        // a proposed broad tolerance of 64, but larger than the existing 8 units.
        Point64 P(long x, long y) => new(origin + x, origin + y);
        var notched = new Path64 { P(0,0), P(100000000,0), P(100000000,100000000),
            P(50000032,100000000), P(50000032,99999984), P(49999968,99999984),
            P(49999968,100000000), P(0,100000000) };
        var flattened = new Path64 { P(0,0), P(100000000,0), P(100000000,100000000), P(0,100000000) };
        if (reverse) { notched.Reverse(); flattened.Reverse(); }
        var method = typeof(PatternTools).GetMethod("LocallyMatches", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        bool Matches(Path64 a, Path64 b, double tolerance) => (bool)method!.Invoke(null, [a, b, tolerance])!;
        Assert.False(Matches(notched, flattened, 8));
        Assert.True(Matches(notched, flattened, 64));
        Assert.True(Matches(flattened, notched, 64));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmoothLobedCoverageFailsSharpFeatureVetoEvenBelowAggregateTolerance(bool reverse)
    {
        var source = Enumerable.Range(0, 512).Select(i =>
        {
            var angle = Math.Tau * i / 512;
            var radius = 1000 + 200 * Math.Cos(3 * angle);
            return new Point2(radius * Math.Cos(angle), radius * Math.Sin(angle));
        }).ToList();
        if (reverse) source.Reverse();
        var original = source.ToArray();
        var path = new PathD(source.Select(p => new PointD(p.X, p.Y)));
        var output = Assert.Single(Clipper.InflatePaths(new PathsD { path }, 1, JoinType.Miter, EndType.Polygon, 2, 6))
            .Select(p => new Point2(p.x, p.y)).ToList();
        var comparison = OffsetShadowDiagnostic.Compare(source, 1, output, .02);
        Assert.True(comparison.Compared);
        Assert.InRange(comparison.CandidateUncoveredMm, 0, .02);
        Assert.InRange(comparison.OutputUncoveredMm, 0, .02);
        Assert.True(comparison.AggregateWithinTolerance);
        Assert.True(comparison.SharpFeatureVeto);
        Assert.False(comparison.FullyCovered);
        Assert.Contains("sharp-feature", comparison.Reason);
        Assert.Equal(original, source);
    }
}
