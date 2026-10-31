using System.Numerics;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public class OffsetMiterIntervalDiagnosticTests
{
    [Theory]
    [InlineData(1, 0L, 0L)]
    [InlineData(-1, 0L, 0L)]
    [InlineData(1, 100000000000000L, -100000000000000L)]
    [InlineData(-1, 100000000000000L, -100000000000000L)]
    public void RightAngle345HasAnalyticalMiterInBothWindingDirections(int direction,long ox,long oy)
    {
        var a=new GridPoint(ox-3,oy-4); var b=new GridPoint(ox,oy); var c=new GridPoint(ox+4,oy-3);
        if(direction<0)(a,c)=(c,a);
        Assert.True(TryBound(a,b,c,5,direction,out var box));
        Assert.True(box.X.ContainsInteger(ox+1));
        Assert.True(box.Y.ContainsInteger(oy-7));
        // Width is far below one grid unit for this exactly conditioned corner.
        Assert.True(box.X.Upper-box.X.Lower < Scale);
        Assert.True(box.Y.Upper-box.Y.Lower < Scale);
    }

    [Fact]
    public void IrrationalMiterIsEnclosedUsingExactSquaredInequality()
    {
        // x=sqrt(2)-1, y=-1 for the 45-degree corner and unit offset.
        Assert.True(TryBound(new(-1,0),new(0,0),new(1,1),1,1,out var box));
        var scale=Scale;
        Assert.True(box.X.Lower+scale > 0);
        Assert.True(BigInteger.Pow(box.X.Lower+scale,2) <= 2*scale*scale);
        Assert.True(BigInteger.Pow(box.X.Upper+scale,2) >= 2*scale*scale);
        Assert.True(box.Y.ContainsInteger(-1));
        Assert.True(box.X.Upper-box.X.Lower < scale);
    }

    [Fact]
    public void DegenerateOrReversalCannotProduceCertificate()
    {
        Assert.False(TryBound(new(0,0),new(0,0),new(1,0),1,1,out _));
        Assert.False(TryBound(new(-1,0),new(0,0),new(-1,0),1,1,out _));
        Assert.False(TryBound(new(-1,0),new(0,0),new(1,1),0,1,out _));
        Assert.False(TryBound(new(-1,0),new(0,0),new(1,1),1,0,out _));
    }
}
