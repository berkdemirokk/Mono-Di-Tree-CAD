using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class OpenPathExactToleranceTests
{
    [Fact]
    public void JoiningRejectsDistanceJustAboveToleranceEvenWhenRoundedLengthEqualsIt()
    {
        double epsilon = Math.ScaleB(1, -27);
        var first = new PatternPiece { Closed = false, Points = [new(-10,0),new(0,0)] };
        var second = new PatternPiece { Closed = false, Points = [new(1,epsilon),new(10,10)] };
        var firstBefore = first.Points.ToArray(); var secondBefore = second.Points.ToArray();
        Assert.Equal(1, Geometry.Distance(first.Points[^1], second.Points[0]));
        Assert.Throws<ArgumentException>(() => OpenPathJoin.Join(first, second, 1));
        var result = OpenPathJoin.Join(first, second, Math.BitIncrement(1));
        Assert.Equal(4, result.Piece.Points.Count);
        Assert.Equal(firstBefore, first.Points); Assert.Equal(secondBefore, second.Points);
    }

    [Fact]
    public void ClosingRejectsDistanceJustAboveToleranceEvenWhenRoundedLengthEqualsIt()
    {
        double epsilon = Math.ScaleB(1, -27);
        var source = new PatternPiece { Closed = false, Points = [new(0,0),new(0,-10),new(10,-10),new(1,epsilon)] };
        var before = source.Points.ToArray();
        Assert.Equal(1, Geometry.Distance(source.Points[0], source.Points[^1]));
        Assert.Throws<ArgumentException>(() => OpenPathClosure.Close(source, 1));
        Assert.True(OpenPathClosure.Close(source, Math.BitIncrement(1)).Piece.Closed);
        Assert.Equal(before, source.Points);
    }
}
