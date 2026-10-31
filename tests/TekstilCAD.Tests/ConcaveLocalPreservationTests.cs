using TekstilCAD.Core;
using TekstilCAD.Patterns;
namespace TekstilCAD.Tests;

public class ConcaveLocalPreservationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LargePerimeterDoesNotMaskLostNarrowRecess(bool reverse)
    {
        var p = new PatternPiece { Points = [new(0,0), new(90000000,0), new(90000000,90000000), new(45000002,90000000), new(45000002,89999920), new(44999998,89999920), new(44999998,90000000), new(0,90000000)] };
        if (reverse) p.Points.Reverse();
        var before = p.Points.ToArray();
        Assert.ThrowsAny<ArgumentException>(() => PatternTools.Offset(p, 5));
        Assert.Equal(before, p.Points);
    }
}
