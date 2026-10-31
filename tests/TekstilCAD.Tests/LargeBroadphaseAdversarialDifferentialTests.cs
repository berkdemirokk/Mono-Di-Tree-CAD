using TekstilCAD.Core;

namespace TekstilCAD.Tests;

/// <summary>Regression comparisons against the existing full-pair validator, not a proof of numerical equivalence.</summary>
public sealed class LargeBroadphaseAdversarialDifferentialTests
{
    private static void AssertEquivalent(IReadOnlyList<Point2> points)
    {
        var before = points.ToArray();
        var full = Record.Exception(() => LargeContourValidator.Validate(points));
        var filtered = Record.Exception(() => LargeContourBroadphaseDiagnostic.Validate(points));
        Assert.Equal(full is null, filtered is null);
        if (full is not null) Assert.Equal(full.GetType(), filtered!.GetType());
        Assert.Equal(before, points);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(50000000.0)]
    [InlineData(-99980000.0)]
    public void SeededRadialPathsAndPermutedCrossingsAgree(double origin)
    {
        for (var seed = 0; seed < 32; seed++)
        {
            var random = new Random(2051 + seed);
            int count = 80 + seed * 3;
            var points = Enumerable.Range(0, count).Select(i =>
            {
                double angle = Math.Tau * i / count;
                double radius = 1000 + 250 * random.NextDouble();
                return new Point2(origin + radius * Math.Cos(angle), origin + radius * Math.Sin(angle));
            }).ToList();
            AssertEquivalent(points);
            var crossing = points.ToList();
            (crossing[1], crossing[count / 2]) = (crossing[count / 2], crossing[1]);
            AssertEquivalent(crossing);
            crossing.Reverse();
            AssertEquivalent(crossing);
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(50000000.0)]
    [InlineData(-99980000.0)]
    public void NearTouchingHorizontalAndVerticalPairsAroundRepresentableEpsilonAgree(double origin)
    {
        // Compute neighbors at the actual coordinate magnitude: at large origins
        // a one-bit step differs significantly from an absolute 1e-8 offset.
        var boundary = origin + Geometry.Epsilon;
        foreach (var ordinate in new[] { Math.BitDecrement(boundary), boundary, Math.BitIncrement(boundary),
                                        Math.BitDecrement(origin), origin, Math.BitIncrement(origin) })
        {
            List<Point2> horizontal = [new(origin, origin), new(origin + 200, origin),
                new(origin + 200, origin + 100), new(origin + 101, origin + 100),
                new(origin + 101, ordinate), new(origin + 99, ordinate),
                new(origin + 99, origin + 100), new(origin, origin + 100)];
            AssertEquivalent(horizontal);
            var vertical = horizontal.Select(p => new Point2(p.Y, p.X)).ToList();
            AssertEquivalent(vertical);
            vertical.Reverse();
            vertical.Add(vertical[0]);
            AssertEquivalent(vertical);
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(50000000.0)]
    [InlineData(-99980000.0)]
    public void LongAndShortNearlyCollinearSegmentsAgreeAcrossOneBitPerturbations(double origin)
    {
        // Long nearly parallel segments magnify cancellation in orientation.
        // The full validator decides whether tolerance makes each case a crossing.
        var baseY = origin + 0.0001;
        foreach (var y in new[] { Math.BitDecrement(baseY), baseY, Math.BitIncrement(baseY) })
        {
            List<Point2> contour = [new(origin, origin), new(origin + 5000, origin + 0.00005),
                new(origin + 5000, y), new(origin + 0.1, origin + 0.00006),
                new(origin + 0.1, origin + 1), new(origin, origin + 1)];
            AssertEquivalent(contour);
            contour.Reverse();
            AssertEquivalent(contour);
        }
    }
}
