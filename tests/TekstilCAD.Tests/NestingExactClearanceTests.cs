using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class NestingExactClearanceTests
{
    [Fact]
    public void ClearanceSmallerThanCoordinateResolutionIsRoundedOutward()
    {
        PatternPiece Square() => new() { Points = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)] };
        var result = NestingEngine.Arrange([Square(), Square()], 4, Math.ScaleB(1d, -54));
        var first = Geometry.Bounds(result.Layout.Pieces[0].Points);
        var second = Geometry.Bounds(result.Layout.Pieces[1].Points);
        Assert.Equal(1d, first.MaxX);
        Assert.Equal(Math.BitIncrement(1d), second.MinX);
        ShelfLayoutVerifier.Verify(result);
    }

    [Fact]
    public void DecimalMarginDoesNotEraseOutwardRoundedGap()
    {
        PatternPiece Piece() => new() { Points = [new(0, 0), new(.2, 0), new(.2, 1), new(0, 1)] };
        var result = NestingEngine.Arrange([Piece(), Piece()], 10, .1, edgeMargin: .1);
        var first = Geometry.Bounds(result.Layout.Pieces[0].Points);
        var second = Geometry.Bounds(result.Layout.Pieces[1].Points);
        Assert.True(second.MinX > .4);
        Assert.True(second.MinX > first.MaxX);
        ShelfLayoutVerifier.Verify(result);
    }
}
