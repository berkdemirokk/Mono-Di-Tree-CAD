using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

/// <summary>Exact axis-aligned examples only; these do not certify general offsets.</summary>
public class OffsetAnalyticConcaveReferenceTests
{
    [Theory]
    [InlineData(false, 0, 0)]
    [InlineData(true, 2, 0)]
    [InlineData(false, 4, 1)]
    [InlineData(true, 5, 1)]
    public void ConcaveLMatchesEveryAnalyticMiterEdge(bool reverse, int start, int quarterTurns)
    {
        // Offset each line by one unit and intersect neighboring lines by hand.
        // The concave corner (4,4) becomes (5,5), not a rounded or closed notch.
        Point2[] source = [new(0,0), new(8,0), new(8,4), new(4,4), new(4,8), new(0,8)];
        Point2[] expected = [new(-1,-1), new(9,-1), new(9,5), new(5,5), new(5,9), new(-1,9)];
        static Point2 Transform(Point2 p, int turns) => turns == 0 ? p : new(-p.Y, p.X);
        var input = source.Select(p => Transform(p, quarterTurns)).ToList();
        var reference = expected.Select(p => Transform(p, quarterTurns)).ToList();
        if (reverse) input.Reverse();
        input = input.Skip(start).Concat(input.Take(start)).ToList();
        var piece = new PatternPiece { Closed = true, Points = input };
        var before = input.ToArray();

        var actual = PatternTools.Offset(piece, 1).Points.ToList();
        if (actual.Count > 1 && actual[0] == actual[^1]) actual.RemoveAt(actual.Count - 1);
        Assert.False(Geometry.HasSelfIntersections(actual));
        Assert.Equal(before, piece.Points);

        // Every reference corner must occur, and each output edge must lie on a
        // reference edge. This permits harmless collinear subdivisions only.
        foreach (var corner in reference)
            Assert.Contains(actual, p => Geometry.Distance(p, corner) <= 0.000001);
        for (var i = 0; i < actual.Count; i++)
        {
            var a = actual[i]; var b = actual[(i + 1) % actual.Count];
            Assert.True(Enumerable.Range(0, reference.Count).Any(j =>
                OnSegment(a, reference[j], reference[(j + 1) % reference.Count]) &&
                OnSegment(b, reference[j], reference[(j + 1) % reference.Count])),
                $"Unexpected output edge {i}: {a} to {b}");
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 3)]
    public void ClosedRecessIsRejectedWithoutChangingSource(bool reverse, int start)
    {
        List<Point2> input = [new(0,0), new(100,0), new(100,100), new(52,100),
            new(52,20), new(48,20), new(48,100), new(0,100)];
        if (reverse) input.Reverse();
        input = input.Skip(start).Concat(input.Take(start)).ToList();
        var piece = new PatternPiece { Closed = true, Points = input };
        var before = input.ToArray();
        Assert.ThrowsAny<ArgumentException>(() => PatternTools.Offset(piece, 5));
        Assert.Equal(before, piece.Points);
    }

    private static bool OnSegment(Point2 p, Point2 a, Point2 b)
    {
        const double tolerance = 0.000001;
        var dx = b.X - a.X; var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        return Geometry.Distance(p, new Point2(a.X + t * dx, a.Y + t * dy)) <= tolerance;
    }
}
