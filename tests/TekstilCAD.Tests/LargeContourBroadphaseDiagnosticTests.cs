using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public sealed class LargeContourBroadphaseDiagnosticTests
{
    private static List<Point2> Ring(int count, double origin, int seed)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, count).Select(i =>
        {
            var angle = Math.Tau * i / count;
            var radius = 1000 + random.NextDouble() * 70;
            return new Point2(origin + radius * Math.Cos(angle), origin + radius * Math.Sin(angle));
        }).ToList();
    }

    private static void Compare(IReadOnlyList<Point2> points)
    {
        var original = points.ToArray();
        var expected = Record.Exception(() => LargeContourValidator.Validate(points));
        var actual = Record.Exception(() => LargeContourBroadphaseDiagnostic.Validate(points));
        Assert.Equal(expected is null, actual is null);
        if (expected is not null) Assert.Equal(expected.GetType(), actual!.GetType());
        Assert.Equal(original, points);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(50000000.0)]
    [InlineData(-99990000.0)]
    public void SeededContoursAndCrossingsAgreeWithFullPairOracle(double origin)
    {
        for (var seed = 0; seed < 12; seed++)
        {
            var points = Ring(24 + seed * 3, origin, seed + 100);
            Compare(points);
            points.Reverse();
            Compare(points);
            var crossing = points.ToList();
            (crossing[2], crossing[crossing.Count / 2]) = (crossing[crossing.Count / 2], crossing[2]);
            Compare(crossing);
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(50000000.0)]
    public void NearTouchCollinearAndRepeatedFeaturesAgreeWithOracle(double origin)
    {
        foreach (var gap in new[] { -2e-8, 0.0, 0.5e-8, 1.0e-8, 2e-8, 1e-5 })
        {
            // Non-adjacent near-touch and nearly collinear edge pairs; the
            // oracle decides the result under its scale-dependent epsilon.
            List<Point2> near = [new(origin,origin), new(origin+100,origin),
                new(origin+100,origin+100), new(origin+50,origin+100),
                new(origin+50,origin+gap), new(origin+49,origin+gap),
                new(origin+49,origin+100), new(origin,origin+100)];
            Compare(near);
            near.Reverse();
            Compare(near);
        }
        List<Point2> backtrack = [new(origin,origin), new(origin+100,origin),
            new(origin+40,origin), new(origin+100,origin+100), new(origin,origin+100)];
        Compare(backtrack);
        List<Point2> duplicate = [new(origin,origin), new(origin+100,origin),
            new(origin+100,origin+100), new(origin+100,origin), new(origin,origin+100)];
        Compare(duplicate);
        List<Point2> closed = [new(origin,origin), new(origin+100,origin),
            new(origin+100,origin+100), new(origin,origin+100), new(origin,origin)];
        Compare(closed);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(99990000.0)]
    public void AdjacentRepresentableValuesAroundEpsilonAndNearlyParallelEdgesAgree(double origin)
    {
        var boundary = origin + Geometry.Epsilon;
        foreach (var y in new[] { Math.BitDecrement(boundary), boundary, Math.BitIncrement(boundary) })
        {
            List<Point2> points = [new(origin,origin), new(origin+1000,origin),
                new(origin+1000,origin+100), new(origin+500,origin+100),
                new(origin+500,y), new(origin+499,y), new(origin+499,origin+100),
                new(origin,origin+100)];
            Compare(points);
            points.Add(points[0]);
            Compare(points);
        }
        List<Point2> almostParallel = [new(origin,origin), new(origin+1000,origin+0.00001),
            new(origin+1000,origin+0.00003), new(origin+0.01,origin+0.00002),
            new(origin,origin+20)];
        Compare(almostParallel);
        almostParallel.Reverse();
        Compare(almostParallel);
    }

    [Fact]
    public void DisjointBoundsAreActuallyFilteredWithoutChangingResult()
    {
        var points = Ring(512, 0, 17);
        LargeContourValidator.Validate(points);
        var stats = LargeContourBroadphaseDiagnostic.Validate(points);
        Assert.True(stats.PairCount > 0);
        Assert.True(stats.BroadphaseRejected > 0);
        Assert.True(stats.PredicateCalls < stats.PairCount);
    }

    [Fact]
    public void PrecancellationDoesNotMutateInputAndSameInputCanBeRetried()
    {
        var points = Ring(64, 50_000_000, 9);
        var original = points.ToArray();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => LargeContourBroadphaseDiagnostic.Validate(points, cancel.Token));
        Assert.Equal(original, points);
        Compare(points);
    }
}
