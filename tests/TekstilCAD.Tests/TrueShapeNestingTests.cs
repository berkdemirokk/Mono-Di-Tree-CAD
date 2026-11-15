using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public class TrueShapeNestingTests
{
    private static PatternPiece Piece(string name, params (double X, double Y)[] points) =>
        new() { Name = name, Closed = true, Points = points.Select(p => new Point2(p.X, p.Y)).ToList() };

    private static PatternPiece L(string name) =>
        Piece(name, (0, 0), (100, 0), (100, 50), (50, 50), (50, 100), (0, 100));

    private static PatternPiece Rectangle(string name, double width, double height) =>
        Piece(name, (0, 0), (width, 0), (width, height), (0, height));

    private static (double MinX, double MinY, double MaxX, double MaxY) Box(TrueShapePlacement placement) =>
        (placement.Points.Min(p => p.X), placement.Points.Min(p => p.Y), placement.Points.Max(p => p.X), placement.Points.Max(p => p.Y));

    [Fact]
    public void ConcavePiecesInterlockAndUseLessLengthThanTheShelfLayout()
    {
        var pieces = new[] { L("a"), L("b") };
        var shelf = NestingEngine.Arrange(pieces, 150, 0);
        var result = TrueShapeNesting.Arrange(pieces, 150, gap: 0, allowRotate180: true, step: 10);
        Assert.Equal(100.0, result.UsedLength);
        Assert.True(result.UsedLength < shelf.UsedLength);
        Assert.False(result.Placements[0].Rotated180);
        Assert.True(result.Placements[1].Rotated180);
        Assert.Equal(new[] { pieces[0].Id, pieces[1].Id }, result.Placements.Select(p => p.PieceId));
        Assert.Equal(100.0, result.UtilizationPercent, 6);
        Assert.All(result.Placements, p => { var box = Box(p); Assert.True(box.MinX >= 0 && box.MaxX <= 150 && box.MinY >= 0); });
    }

    [Fact]
    public void WithoutRotationTheSecondConcavePieceStillAvoidsTheFirst()
    {
        var result = TrueShapeNesting.Arrange(new[] { L("a"), L("b") }, 150, gap: 0, allowRotate180: false, step: 10);
        Assert.All(result.Placements, p => Assert.False(p.Rotated180));
        Assert.Equal(150.0, result.UsedLength);
    }

    [Fact]
    public void GapAndEdgeMarginAreRespectedForRectangles()
    {
        var pieces = new[] { Rectangle("a", 40, 30), Rectangle("b", 40, 30), Rectangle("c", 40, 30) };
        var result = TrueShapeNesting.Arrange(pieces, 120, gap: 5, edgeMargin: 10, step: 1);
        var boxes = result.Placements.Select(Box).ToArray();
        Assert.All(boxes, box => Assert.True(box.MinX >= 10 && box.MaxX <= 110 && box.MinY >= 0));
        for (var i = 0; i < boxes.Length; i++)
            for (var j = i + 1; j < boxes.Length; j++)
            {
                var separatedX = boxes[i].MaxX + 5 <= boxes[j].MinX || boxes[j].MaxX + 5 <= boxes[i].MinX;
                var separatedY = boxes[i].MaxY + 5 <= boxes[j].MinY || boxes[j].MaxY + 5 <= boxes[i].MinY;
                Assert.True(separatedX || separatedY);
            }
        Assert.Equal(65.0, result.UsedLength);
    }

    [Fact]
    public void SameInputGivesTheSameLayout()
    {
        var pieces = new[] { L("a"), Rectangle("b", 60, 20), L("c"), Rectangle("d", 30, 30) };
        var first = TrueShapeNesting.Arrange(pieces, 200, gap: 2, allowRotate180: true, step: 5);
        var second = TrueShapeNesting.Arrange(pieces, 200, gap: 2, allowRotate180: true, step: 5);
        Assert.Equal(first.UsedLength, second.UsedLength);
        Assert.Equal(first.Placements.Select(p => (p.OffsetX, p.OffsetY, p.Rotated180)), second.Placements.Select(p => (p.OffsetX, p.OffsetY, p.Rotated180)));
    }

    [Fact]
    public void InvalidInputIsRejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => TrueShapeNesting.Arrange(new[] { Rectangle("wide", 200, 10) }, 150));
        var open = Rectangle("open", 10, 10); open.Closed = false;
        Assert.ThrowsAny<ArgumentException>(() => TrueShapeNesting.Arrange(new[] { open }, 150));
        Assert.Throws<ArgumentException>(() => TrueShapeNesting.Arrange(Array.Empty<PatternPiece>(), 150));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrueShapeNesting.Arrange(new[] { Rectangle("a", 10, 10) }, 150, step: 0));
    }
}
