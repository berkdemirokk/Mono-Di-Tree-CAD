using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class SmoothAllowanceRegressionTests
{
    private static PatternPiece Radial(int count, bool lobed)
    {
        var points = new List<Point2>();
        for (int i = 0; i < count; i++)
        {
            double a = 2 * Math.PI * i / count;
            double radius = 1000 + (lobed ? 200 * Math.Cos(3 * a) : 0);
            points.Add(new Point2(radius * Math.Cos(a), radius * Math.Sin(a)));
        }
        return new PatternPiece { Name = "Yoğun kontur", Closed = true, Points = points };
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DenseSmoothContoursPreserveShapeAndExpandByOneMillimeter(bool lobed, bool reverse)
    {
        var source = Radial(512, lobed);
        if (reverse) source.Points.Reverse();
        var before = source.Points.ToArray();
        var bounds = Geometry.Bounds(before);
        var result = PatternTools.Offset(source, 1);
        var actual = Geometry.Bounds(result.Points);
        // A polygonal miter can extend slightly past the normal distance at a vertex.
        Assert.InRange(bounds.MinX - actual.MinX, .999, 1.01);
        Assert.InRange(bounds.MinY - actual.MinY, .999, 1.01);
        Assert.InRange(actual.MaxX - bounds.MaxX, .999, 1.01);
        Assert.InRange(actual.MaxY - bounds.MaxY, .999, 1.01);
        Assert.True(Math.Abs(Geometry.SignedArea(result.Points)) > Math.Abs(Geometry.SignedArea(before)));
        Assert.False(Geometry.HasSelfIntersections(result.Points));
        Assert.Equal(before, source.Points);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DenseCompatibleChangeDoesNotPermitClosedNarrowRecess(bool reverse)
    {
        var source = new PatternPiece { Points = [new(0,0), new(100,0), new(100,100), new(52,100), new(52,20), new(48,20), new(48,100), new(0,100)] };
        if (reverse) source.Points.Reverse();
        var before = source.Points.ToArray();
        Assert.ThrowsAny<ArgumentException>(() => PatternTools.Offset(source, 5));
        Assert.Equal(before, source.Points);
    }
}
