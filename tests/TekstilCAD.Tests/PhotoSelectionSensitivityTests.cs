using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public class PhotoSelectionSensitivityTests
{
    [Fact]
    public void ZeroAssumedErrorProducesTheNominalLength()
    {
        var result=PhotoSelectionSensitivity.Estimate(100,100,100,100,new(10,10),new(60,10),100,new(20,30),new(45,30),0);
        Assert.Equal(50,result.LowerMm,10);Assert.Equal(50,result.UpperMm!.Value,10);
    }
    [Fact]
    public void EnvelopeContainsSyntheticEndpointPerturbationsWithUnequalPreviewRatios()
    {
        var a=new Point2(100,100);var b=new Point2(300,100);var c=new Point2(160,140);var d=new Point2(240,190);
        var result=PhotoSelectionSensitivity.Estimate(512,256,2001,1000,a,b,200,c,d,1);
        foreach(var ax in new[] {-1,0,1})foreach(var ay in new[] {-1,0,1})
        foreach(var bx in new[] {-1,0,1})foreach(var by in new[] {-1,0,1})
        foreach(var cx in new[] {-1,0,1})foreach(var cy in new[] {-1,0,1})
        foreach(var dx in new[] {-1,0,1})foreach(var dy in new[] {-1,0,1})
        {
            var actual=PhotoTraceCalibration.Convert(512,256,2001,1000,new(a.X+ax,a.Y+ay),new(b.X+bx,b.Y+by),200,
                [new(c.X+cx,c.Y+cy),new(d.X+dx,d.Y+dy)]);
            var length=Geometry.Distance(actual[0],actual[1]);
            Assert.InRange(length,result.LowerMm-1e-9,result.UpperMm!.Value+1e-9);
        }
    }
    [Fact]
    public void ShortReferenceMayHaveNoFiniteUpperBound()
    {
        var result=PhotoSelectionSensitivity.Estimate(100,100,100,100,new(10,10),new(11,10),100,new(20,20),new(30,20),1);
        Assert.Null(result.UpperMm);Assert.True(result.LowerMm>=0);
    }
    [Theory]
    [InlineData(-1)] [InlineData(1001)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidAssumptionsAreRejected(double pixels)
        =>Assert.Throws<ArgumentOutOfRangeException>(()=>PhotoSelectionSensitivity.Estimate(100,100,100,100,new(10,10),new(20,10),100,new(20,20),new(30,20),pixels));
    [Fact]
    public void InvalidSourceGeometryIsNotAccepted()
        =>Assert.ThrowsAny<ArgumentException>(()=>PhotoSelectionSensitivity.Estimate(100,100,100,100,new(10,10),new(10,10),100,new(20,20),new(30,20),1));
}
