using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public class NestingEdgeMarginTests
{
    private static PatternPiece Rectangle(double width = 40, double height = 20) => new()
    {
        Name = "Parça", Points = [new(100,200), new(100+width,200), new(100+width,200+height), new(100,200+height)]
    };

    [Fact]
    public void MarginAppliesToAllFabricEdgesAndGapRemainsBetweenPieces()
    {
        var source = new[] { Rectangle(), Rectangle(), Rectangle() };
        var result = NestingEngine.Arrange(source, 100, gap: 10, edgeMargin: 5);
        var boxes = result.Layout.Pieces.Select(p => Geometry.Bounds(p.Points)).ToArray();
        Assert.Equal((5d,5d,45d,25d), (boxes[0].MinX,boxes[0].MinY,boxes[0].MaxX,boxes[0].MaxY));
        Assert.Equal((55d,5d,95d,25d), (boxes[1].MinX,boxes[1].MinY,boxes[1].MaxX,boxes[1].MaxY));
        Assert.Equal((5d,35d,45d,55d), (boxes[2].MinX,boxes[2].MinY,boxes[2].MaxX,boxes[2].MaxY));
        Assert.Equal(60, result.UsedLength);
        Assert.Equal(40, result.UtilizationPercent, 8);
        Assert.Equal(60, result.WastePercent, 8);
        Assert.Equal(5, result.EdgeMargin);
        Assert.All(source, p => Assert.Equal(new Point2(100,200), p.Points[0]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(50)]
    [InlineData(100000001)]
    public void InvalidMarginIsRejected(double margin) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => NestingEngine.Arrange([Rectangle()], 100, edgeMargin: margin));

    [Fact]
    public void AvailableWidthAndTotalLengthIncludeBothMargins()
    {
        var exact = Rectangle(90,20);
        var result = NestingEngine.Arrange([exact], 100, gap: 0, maxLength: 30, edgeMargin: 5);
        var bounds = Geometry.Bounds(Assert.Single(result.Layout.Pieces).Points);
        Assert.Equal(5, bounds.MinX); Assert.Equal(95, bounds.MaxX); Assert.Equal(30, result.UsedLength);
        Assert.Throws<ArgumentException>(() => NestingEngine.Arrange([Rectangle(90.01,20)], 100, edgeMargin: 5));
        Assert.Throws<InvalidOperationException>(() => NestingEngine.Arrange([exact], 100, maxLength: 29.99, edgeMargin: 5));
    }

    [Fact]
    public void ZeroMarginAndEmptyJobKeepPreviousSemantics()
    {
        var p = Rectangle();
        var old = NestingEngine.Arrange([p], 100, 5);
        var explicitZero = NestingEngine.Arrange([p], 100, 5, edgeMargin: 0);
        Assert.Equal(old.Layout.Pieces[0].Points, explicitZero.Layout.Pieces[0].Points);
        Assert.Equal(old.UsedLength, explicitZero.UsedLength);
        var empty = NestingEngine.Arrange([], 100, edgeMargin: 5);
        Assert.Equal(0, empty.UsedLength); Assert.Equal(0, empty.UtilizationPercent); Assert.Equal(0, empty.WastePercent);
    }

    [Fact]
    public void MarginSmallerThanCoordinateResolutionCannotDisappearSilently()
    {
        // 1e-16 mm is rounded away at 100 mm and at the large used length.
        Assert.Throws<InvalidOperationException>(() => NestingEngine.Arrange([Rectangle(100,20)], 100, edgeMargin: 1e-16));
        var result = NestingEngine.Arrange([Rectangle(10,99_999_000)], 100, edgeMargin: 1e-16);
        var bounds = Geometry.Bounds(Assert.Single(result.Layout.Pieces).Points);
        Assert.True(result.UsedLength - bounds.MaxY >= 1e-16);
    }

    [Fact]
    public void NonIntegralMarginIsActuallyPresentAtEveryEdge()
    {
        var result = NestingEngine.Arrange([Rectangle(10.25,20.5)], 100.75, edgeMargin: 0.125);
        var b = Geometry.Bounds(Assert.Single(result.Layout.Pieces).Points);
        Assert.True(b.MinX >= .125 && result.FabricWidth - b.MaxX >= .125);
        Assert.True(b.MinY >= .125 && result.UsedLength - b.MaxY >= .125);
    }

    [Fact]
    public void PositiveGapIsPreservedOrExplicitlyRejectedAtPrecisionLimits()
    {
        foreach (var margin in new[] { 0d, 1d })
        {
            var result = NestingEngine.Arrange([Rectangle(),Rectangle()], 100, gap: 1e-16, edgeMargin: margin);
            var bounds = result.Layout.Pieces.Select(p => Geometry.Bounds(p.Points)).ToArray();
            Assert.True(bounds[1].MinX - bounds[0].MaxX >= 1e-16);
        }
        var rows = NestingEngine.Arrange([Rectangle(90),Rectangle(90)], 100, gap: 1e-16, edgeMargin: 5);
        var rowBounds = rows.Layout.Pieces.Select(p => Geometry.Bounds(p.Points)).ToArray();
        Assert.True(rowBounds[1].MinY - rowBounds[0].MaxY >= 1e-16);
    }

    [Fact]
    public void CommonDecimalMarginAndGapAreConservativelyRoundedOutward()
    {
        var result = NestingEngine.Arrange([Rectangle(),Rectangle()], 100, gap: .1, edgeMargin: .2);
        var b = result.Layout.Pieces.Select(p => Geometry.Bounds(p.Points)).ToArray();
        Assert.True(b[0].MinX >= .2 && result.FabricWidth - b[1].MaxX >= .2);
        Assert.True(b[1].MinX - b[0].MaxX >= .1);
        Assert.True(b[0].MinY >= .2 && result.UsedLength - b[0].MaxY >= .2);
    }

    [Fact]
    public void ProductionCopiesUseSameMarginsAndLeaveSourceUntouched()
    {
        var p = Rectangle(); p.Metadata = new() { Role = "Kesim", Quantity = 2, Material = "Pamuk" };
        var source = new ProjectDocument { Pieces = [p] };
        var result = ProductionNestingPlan.Arrange(source, [p.Id], 100, gap: 10, edgeMargin: 5);
        Assert.Equal(2, result.Placements.Count);
        Assert.Equal(5, result.Nesting.EdgeMargin);
        Assert.Equal(30, result.Nesting.UsedLength);
        Assert.All(result.Nesting.Layout.Pieces, placed => {
            var bounds = Geometry.Bounds(placed.Points);
            Assert.InRange(bounds.MinX, 5, 95); Assert.InRange(bounds.MaxX, 5, 95);
            Assert.Equal(5, bounds.MinY); Assert.Equal(25, bounds.MaxY);
        });
        Assert.Equal(2, p.Metadata.Quantity); Assert.Equal(new Point2(100,200), p.Points[0]);
    }
}
