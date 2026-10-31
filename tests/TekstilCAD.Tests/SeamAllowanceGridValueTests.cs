using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class SeamAllowanceGridValueTests
{
    private static PatternPiece Square() => new() { Name="Kare",Points=[new(0,0),new(100,0),new(100,100),new(0,100)] };

    [Theory]
    [InlineData(1.0000004)]
    [InlineData(1.0000006)]
    [InlineData(0.0000015)]
    public void NameAndBoundsUseSameRoundedAllowanceAsCalculation(double requested)
    {
        var source=Square(); var original=source.Points.ToArray();
        var expected=Math.Round(requested*1_000_000,MidpointRounding.AwayFromZero)/1_000_000;
        Assert.Equal(expected,PatternTools.QuantizeAllowance(requested));
        var result=PatternTools.Offset(source,requested);
        var bounds=Geometry.Bounds(result.Points);
        Assert.InRange(Math.Abs(bounds.MinX+expected),0,0.00000001);
        Assert.Contains($"istenen {requested:G} mm",result.Name);
        Assert.Contains($"hesaplanan {expected:G} mm",result.Name);
        Assert.Equal(original,source.Points);
    }

    [Fact]
    public void ZeroAllowanceRetainsExistingNameAndGeometry()
    {
        var source=Square(); var result=PatternTools.Offset(source,0);
        Assert.Equal("Kare · dikiş payı 0 mm",result.Name);
        Assert.Equal(0,PatternTools.QuantizeAllowance(0));
        Assert.Equal(source.Points,result.Points);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0.0000001)]
    [InlineData(100001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void GridHelperAndOffsetBothRejectUnsupportedValues(double value)
    {
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.QuantizeAllowance(value));
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.Offset(Square(),value));
    }

    [Fact]
    public void GeneratedNameStillRespectsLengthLimit()
    {
        var source=Square(); source.Name=new string('A',1024);
        var before=source.Points.ToArray();
        Assert.Throws<ArgumentException>(()=>PatternTools.Offset(source,1));
        Assert.Equal(before,source.Points);
    }
}
