using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class PatternReviewTests
{
    private static PatternPiece Square() => new() { Points = [new(0,0),new(100,0),new(100,100),new(0,100)] };

    [Fact]
    public void ExplicitClosureOffsetsWithoutDuplicateVertex()
    {
        var piece = Square(); piece.Points.Add(piece.Points[0]);
        var result = PatternTools.OffsetConvex(piece, 2.5);
        Assert.Equal(4, result.Points.Count);
        Assert.Equal(105, Geometry.Bounds(result.Points).Width, 8);
        Assert.Equal(5, piece.Points.Count);
    }

    [Fact]
    public void GeneratedNameCannotExceedPersistenceLimit()
    {
        var source = Square(); source.Name = new string('a', 1024);
        Assert.Throws<ArgumentException>(() => PatternTools.OffsetConvex(source, 10));
        Assert.Equal(100, Geometry.Bounds(source.Points).Width);
    }

    [Fact]
    public void NullSourceFieldsAreRejectedExplicitly()
    {
        var source = Square(); source.Name = null!;
        Assert.Throws<ArgumentException>(() => PatternTools.DeletePoint(source, 1));
        source = Square(); source.Points = null!;
        Assert.Throws<ArgumentException>(() => PatternTools.InsertPoint(source, 0, new(50,0)));
    }

    [Fact]
    public void OpenEditingAcceptsLargeValidatedPathsButRejectsSourcesAboveStoredPointLimit()
    {
        var source = new PatternPiece { Closed = false, Points = Enumerable.Range(0, Geometry.MaxValidatedContourVertices + 1).Select(i => new Point2(i, 0)).ToList() };
        var original = source.Points.ToArray();
        var result = PatternTools.DeletePoint(source, 1);
        Assert.Equal(Geometry.MaxValidatedContourVertices, result.Points.Count);
        Assert.Equal(original, source.Points);
        var oversized = new PatternPiece { Closed = false, Points = Enumerable.Range(0, LargeOpenPathValidator.MaximumStoredPoints + 1).Select(i => new Point2(i, 0)).ToList() };
        var oversizedBefore = oversized.Points.ToArray();
        Assert.Throws<ArgumentException>(() => PatternTools.DeletePoint(oversized, 1));
        Assert.Equal(oversizedBefore, oversized.Points);
    }

    [Fact]
    public void ReversedEdgeNotchPointsToItsOwnLeft()
    {
        var notch = PatternTools.NotchPoints(new(100,0), new(0,0), .5, 5);
        Assert.Equal(new Point2(55,0), notch[0]);
        Assert.Equal(new Point2(50,-5), notch[1]);
        Assert.Equal(new Point2(45,0), notch[2]);
    }

    [Fact]
    public void NegativeArcSweepIncludesExpectedEndpoint()
    {
        var points = PatternTools.SampleArc(new(0,0), 20, 90, -90, 12);
        Assert.Equal(0, points[0].X, 8);
        Assert.Equal(20, points[0].Y, 8);
        Assert.Equal(20, points[^1].X, 8);
        Assert.Equal(0, points[^1].Y, 8);
    }

    [Fact]
    public void AllowanceCannotEscapeCoordinateDomainOrMutateSource()
    {
        var source = Square(); source.Points = Geometry.Translate(source.Points, Geometry.MaxCoordinate - 100, 0);
        var before = source.Points.ToList();
        Assert.Throws<ArgumentOutOfRangeException>(() => PatternTools.OffsetConvex(source, 10));
        Assert.Equal(before, source.Points);
    }
}
