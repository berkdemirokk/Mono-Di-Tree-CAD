using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class OffsetFeaturePreservationTests
{
    // Both re-entrant grooves are narrower than twice the allowance.  Their depths
    // differ dramatically: a global area budget must not hide the shallow groove.
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 50000000)]
    [InlineData(true, true, 50000000)]
    public void NarrowGrooveMustNotDisappear(bool shallow, bool reverse, double shift)
    {
        var depth = shallow ? 0.02 : 80.0;
        var piece = new PatternPiece { Points = [
            new(shift,shift), new(shift+100,shift), new(shift+100,shift+100),
            new(shift+52,shift+100), new(shift+52,shift+100-depth),
            new(shift+48,shift+100-depth), new(shift+48,shift+100), new(shift,shift+100)
        ] };
        if (reverse) piece.Points.Reverse();
        var before=piece.Points.ToArray();
        Assert.ThrowsAny<ArgumentException>(() => PatternTools.Offset(piece, 5));
        Assert.Equal(before, piece.Points);
    }

    // Analytically known simple concave step: 10000 - 40*40 source area,
    // outward 5 mm miters yield 110*110 - 40*40 = 10500 mm².
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WideConcavityKeepsIndependentExpectedArea(bool reverse)
    {
        var source = new PatternPiece { Points = [new(0,0),new(100,0),new(100,60),new(60,60),new(60,100),new(0,100)] };
        if (reverse) source.Points.Reverse();
        var before=source.Points.ToArray();
        var result=PatternTools.Offset(source,5);
        var bounds=Geometry.Bounds(result.Points);
        Assert.Equal(-5,bounds.MinX,5); Assert.Equal(105,bounds.MaxX,5);
        Assert.Equal(-5,bounds.MinY,5); Assert.Equal(105,bounds.MaxY,5);
        Assert.Equal(10500,Math.Abs(Geometry.SignedArea(result.Points)),4);
        Assert.False(Geometry.HasSelfIntersections(result.Points));
        Assert.Equal(before,source.Points);
    }
}
