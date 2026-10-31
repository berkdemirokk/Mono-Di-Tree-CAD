using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class OffsetOneGridNotchRegressionTests
{
    [Theory]
    [InlineData(0d, false)]
    [InlineData(0d, true)]
    [InlineData(50_000_000d, false)]
    [InlineData(50_000_000d, true)]
    public void OneGridRecessCannotBeSilentlyLost(double origin, bool reverse)
    {
        // One production grid unit is 0.000001 mm. This recess is one grid
        // unit deep and two units wide, on an otherwise large rectangle.
        var piece = new PatternPiece { Name = "Tek grid girinti", Closed = true, Points =
        [
            new(origin, origin), new(origin + 100, origin), new(origin + 100, origin + 100),
            new(origin + 50.000002, origin + 100), new(origin + 50.000002, origin + 99.999999),
            new(origin + 50, origin + 99.999999), new(origin + 50, origin + 100), new(origin, origin + 100)
        ] };
        if (reverse) piece.Points.Reverse();
        var before = piece.Points.ToArray();
        Geometry.ValidateContour(before); // Rejection must not be caused by malformed input.
        Assert.ThrowsAny<ArgumentException>(() => PatternTools.Offset(piece, 1));
        Assert.Equal(before, piece.Points);
    }
}
