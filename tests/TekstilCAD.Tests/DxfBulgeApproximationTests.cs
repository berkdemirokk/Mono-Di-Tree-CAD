using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfBulgeApproximationTests
{
    private static DxfBulgeApproximationResult Run(double bulge) => DxfBulgeApproximation.Approximate(
        [new(new(0,0),bulge),new(new(2,0),0)],false,1,0.08,1000);
    [Theory]
    [InlineData(1.0, -1)]
    [InlineData(-1.0, 1)]
    public void SemicircleKeepsEndpointsAndOrientation(double bulge, int expectedYSign)
    {
        var result=Run(bulge);
        Assert.Equal(new Point2(0,0),result.Points[0]);
        Assert.Equal(new Point2(2,0),result.Points[^1]);
        Assert.True(result.AddedVertices>0);
        Assert.All(result.Points.Skip(1).SkipLast(1), p=>Assert.True(expectedYSign*p.Y>0));
        Assert.True(result.Points.Skip(1).SkipLast(1).Max(p=>Math.Abs(p.Y))>0.9);
    }
    [Fact]
    public void MajorArcAndScaleProduceExpectedGeometry()
    {
        var result=DxfBulgeApproximation.Approximate([new(new(0,0),2),new(new(2,0),0)],false,2,0.05,1000);
        Assert.Equal(new Point2(4,0),result.Points[^1]);
        Assert.True(result.Points.Min(p=>p.Y)<-3.5);
    }
    [Fact]
    public void ClosedLastBulgeIsSampledButOpenLastBulgeIsRejected()
    {
        DxfBulgeVertex[] source=[new(new(0,0),0),new(new(2,0),0),new(new(2,2),1)];
        var original=source.ToArray();
        var closed=DxfBulgeApproximation.Approximate(source,true,1,0.1,1000);
        Assert.True(closed.AddedVertices>0);
        Assert.NotEqual(closed.Points[0],closed.Points[^1]);
        Assert.Equal(original,source);
        Assert.Throws<InvalidDataException>(()=>DxfBulgeApproximation.Approximate(source,false,1,0.1,1000));
    }
    [Fact]
    public void LinearPathsPreserveRepeatedClosingPointAndScale()
    {
        DxfBulgeVertex[] source=[new(new(0,0),0),new(new(1,0),0),new(new(0,0),0)];
        var result=DxfBulgeApproximation.Approximate(source,true,10,0.1,3);
        Assert.Equal(new[]{new Point2(0,0),new Point2(10,0),new Point2(0,0)},result.Points);
        Assert.Equal(0,result.AddedVertices);
    }
    [Fact]
    public void InvalidOrUnresolvableCurvesAndBudgetFailWithoutResults()
    {
        Assert.Throws<InvalidDataException>(()=>DxfBulgeApproximation.Approximate([new(new(0,0),1),new(new(0,0),0)],false,1,0.1,100));
        Assert.Throws<InvalidDataException>(()=>DxfBulgeApproximation.Approximate([new(new(0,0),1),new(new(2,0),0)],false,1,1e-12,2));
        Assert.Throws<InvalidDataException>(()=>DxfBulgeApproximation.Approximate([new(new(0,0),double.NaN),new(new(2,0),0)],false,1,0.1,100));
        Assert.Throws<ArgumentOutOfRangeException>(()=>DxfBulgeApproximation.Approximate([new(new(0,0),0),new(new(1,0),0)],false,1,0,100));
        using var cts=new CancellationTokenSource();cts.Cancel();
        Assert.Throws<OperationCanceledException>(()=>DxfBulgeApproximation.Approximate([new(new(0,0),1),new(new(2,0),0)],false,1,0.1,100,cts.Token));
    }
}
