using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

/// <summary>
/// Hand-derived, axis-aligned references. These examples do not establish
/// general offset correctness or a manufacturing tolerance.
/// </summary>
public sealed class OffsetIndependentOracleTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 4)]
    public void ConcaveLHasEveryExpectedBoundaryEdge(bool reverse, int start)
    {
        Point2[] source = [new(0,0),new(8,0),new(8,4),new(4,4),new(4,8),new(0,8)];
        // Outward normals and orthogonal line intersections at distance 1.
        Point2[] expected = [new(-1,-1),new(9,-1),new(9,5),new(5,5),new(5,9),new(-1,9)];
        AssertOffsetMatches(source, expected, 1, reverse, start);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 5)]
    public void WideURecessKeepsItsHandDerivedBoundary(bool reverse, int start)
    {
        Point2[] source = [new(0,0),new(100,0),new(100,100),new(60,100),
            new(60,20),new(40,20),new(40,100),new(0,100)];
        // A 2 mm outward offset narrows the opening from 20 to 16 mm and
        // raises its bottom by 2 mm; the opening must not disappear.
        Point2[] expected = [new(-2,-2),new(102,-2),new(102,102),new(58,102),
            new(58,22),new(42,22),new(42,102),new(-2,102)];
        AssertOffsetMatches(source, expected, 2, reverse, start);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 5)]
    public void ClosedNarrowRecessIsRejectedWithoutSourceMutation(bool reverse, int start)
    {
        // The 4 mm opening closes under a 5 mm outward offset.
        Point2[] source = [new(0,0),new(100,0),new(100,100),new(52,100),
            new(52,20),new(48,20),new(48,100),new(0,100)];
        var piece = Piece(source, reverse, start);
        var before = piece.Points.ToArray();
        Assert.ThrowsAny<ArgumentException>(() => PatternTools.Offset(piece, 5));
        Assert.Equal(before, piece.Points);
    }

    private static PatternPiece Piece(Point2[] points, bool reverse, int start)
    {
        var ordered = points.ToList();
        if (reverse) ordered.Reverse();
        return new PatternPiece { Closed = true, Points = ordered.Skip(start).Concat(ordered.Take(start)).ToList() };
    }

    private static void AssertOffsetMatches(Point2[] source, Point2[] expected, double allowance, bool reverse, int start)
    {
        var piece = Piece(source, reverse, start);
        var before = piece.Points.ToArray();
        var result = PatternTools.Offset(piece, allowance);
        var actual = result.Points.ToList();
        if (actual.Count > 1 && actual[0] == actual[^1]) actual.RemoveAt(actual.Count - 1);
        Assert.True(actual.Count >= expected.Length);
        Assert.False(Geometry.HasSelfIntersections(actual));
        Assert.Equal(before, piece.Points);
        foreach (var corner in expected)
            Assert.Contains(actual, point => Geometry.Distance(point, corner) <= 0.000001);
        for (var i = 0; i < actual.Count; i++)
        {
            var a=actual[i]; var b=actual[(i+1)%actual.Count];
            Assert.True(Enumerable.Range(0,expected.Length).Any(j =>
                OnAxisSegment(a,expected[j],expected[(j+1)%expected.Length]) &&
                OnAxisSegment(b,expected[j],expected[(j+1)%expected.Length])),
                $"Output edge {i} is not on a hand-derived reference edge: {a}–{b}");
        }
    }

    private static bool OnAxisSegment(Point2 point, Point2 start, Point2 end)
    {
        const double grid = 0.000001;
        if (start.X == end.X)
            return Math.Abs(point.X-start.X)<=grid && point.Y>=Math.Min(start.Y,end.Y)-grid && point.Y<=Math.Max(start.Y,end.Y)+grid;
        if (start.Y == end.Y)
            return Math.Abs(point.Y-start.Y)<=grid && point.X>=Math.Min(start.X,end.X)-grid && point.X<=Math.Max(start.X,end.X)+grid;
        throw new InvalidOperationException("Reference boundary is not axis-aligned.");
    }
}
