using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

/// <summary>
/// Analytic L-shaped example rotated and scaled by a 3-4-5 transform. This
/// exercises diagonal edges but is not a certificate for arbitrary contours.
/// </summary>
public class OffsetAnalyticDiagonalConcaveTests
{
    [Theory]
    [InlineData(false, 0, 0.0)]
    [InlineData(true, 2, 0.0)]
    [InlineData(false, 4, 50000000.0)]
    [InlineData(true, 5, -50000000.0)]
    public void RotatedConcaveLMiterMatchesAnalyticReference(bool reverse, int start, double origin)
    {
        // T is a rotation followed by uniform scale 5. Thus the unit-offset
        // reference below transforms to the 5 mm offset of the transformed input.
        Point2 Transform(Point2 p) => new(origin + 3 * p.X - 4 * p.Y, origin + 4 * p.X + 3 * p.Y);
        Point2[] source = [new(0,0), new(8,0), new(8,4), new(4,4), new(4,8), new(0,8)];
        Point2[] reference = [new(-1,-1), new(9,-1), new(9,5), new(5,5), new(5,9), new(-1,9)];
        var input = source.Select(Transform).ToList();
        var expected = reference.Select(Transform).ToList();
        if (reverse) input.Reverse();
        input = input.Skip(start).Concat(input.Take(start)).ToList();
        var before = input.ToArray();
        var piece = new PatternPiece { Closed = true, Points = input };

        var actual = PatternTools.Offset(piece, 5).Points.ToList();
        if (actual.Count > 1 && actual[0] == actual[^1]) actual.RemoveAt(actual.Count - 1);
        Assert.Equal(before, piece.Points);
        Assert.False(Geometry.HasSelfIntersections(actual));
        foreach (var corner in expected)
            Assert.Contains(actual, p => Geometry.Distance(p, corner) <= 0.000002);
        for (var i = 0; i < actual.Count; i++)
        {
            var a = actual[i]; var b = actual[(i + 1) % actual.Count];
            Assert.True(Enumerable.Range(0, expected.Count).Any(j =>
                OnSegment(a, expected[j], expected[(j + 1) % expected.Count]) &&
                OnSegment(b, expected[j], expected[(j + 1) % expected.Count])),
                $"Unexpected output edge {i}: {a} to {b}");
        }
    }

    private static bool OnSegment(Point2 p, Point2 a, Point2 b)
    {
        const double tolerance = 0.000002;
        var dx = b.X - a.X; var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        return Geometry.Distance(p, new Point2(a.X + t * dx, a.Y + t * dy)) <= tolerance;
    }
}
