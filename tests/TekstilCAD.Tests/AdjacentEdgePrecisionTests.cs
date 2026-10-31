using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public sealed class AdjacentEdgePrecisionTests
{
    [Fact]
    public void ShortFoldbackIsRejectedInBothDirections()
    {
        Point2[] points = [new(0, 0), new(0.0001, 0), new(0.00005, 0)];
        CheckOpen(points, true);
        CheckOpen(points.Reverse().ToArray(), true);
    }

    [Fact]
    public void ShortNoncollinearTurnIsAcceptedInBothDirections()
    {
        Point2[] points = [new(0, 0), new(0.0001, 0), new(0.00005, 0.000001)];
        CheckOpen(points, false);
        CheckOpen(points.Reverse().ToArray(), false);
    }

    [Fact]
    public void ExistingConservativeNearFoldbackIsRejectedInBothDirections()
    {
        Point2[] points = [new(0, 0), new(1, 0), new(0.5, 0.000000001)];
        CheckOpen(points, true);
        CheckOpen(points.Reverse().ToArray(), true);
    }

    [Fact]
    public void LargeNearlyCollinearRaysAgreeAcrossValidatorsWhenProductsRoundToSameValue()
    {
        // The exact determinant of these integer coordinates is -1, although
        // the two binary64 products both round to the same value.
        Point2[] points = [new(100_000_000, 99_999_999), new(0, 0), new(99_999_999, 99_999_998)];
        // Existing tolerance may conservatively reject this near-foldback;
        // both implementations and traversal directions must agree.
        Assert.Equal(Geometry.HasSelfIntersections(points, false),
            !Accepts(() => LargeOpenPathValidator.Validate(points)));
        var reversed = points.Reverse().ToArray();
        Assert.Equal(Geometry.HasSelfIntersections(reversed, false),
            !Accepts(() => LargeOpenPathValidator.Validate(reversed)));
    }

    [Fact]
    public void ExactLargeCollinearRaysOverlap()
    {
        Point2[] points = [new(100_000_000, 100_000_000), new(0, 0), new(50_000_000, 50_000_000)];
        CheckOpen(points, true);
        CheckOpen(points.Reverse().ToArray(), true);
    }

    [Fact]
    public void ClosedValidatorsAgreeOnShortCornerAndFoldbackInBothDirections()
    {
        Point2[][] cases =
        [
            [new(0, 0), new(0.0001, 0), new(0.00005, 0.00001), new(1, 1), new(0, 1)],
            [new(0, 0), new(0.0001, 0), new(0.00005, 0), new(1, 1), new(0, 1)]
        ];
        foreach (var source in cases)
        foreach (var points in new[] { source, source.Reverse().ToArray() })
        {
            var accepted = Accepts(() => Geometry.ValidateContour(points));
            Assert.Equal(accepted, Accepts(() => LargeContourValidator.Validate(points)));
            Assert.Equal(accepted, Accepts(() => LargeContourBroadphaseDiagnostic.Validate(points)));
        }
        Assert.True(Accepts(() => Geometry.ValidateContour(cases[0])));
        Assert.False(Accepts(() => Geometry.ValidateContour(cases[1])));
    }

    private static void CheckOpen(Point2[] points, bool intersects)
    {
        Assert.Equal(intersects, Geometry.HasSelfIntersections(points, false));
        Assert.Equal(!intersects, Accepts(() => LargeOpenPathValidator.Validate(points)));
    }

    private static bool Accepts(Action action)
    {
        try { action(); return true; }
        catch (ArgumentException) { return false; }
    }
}
