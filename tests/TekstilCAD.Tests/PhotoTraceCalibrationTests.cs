using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public class PhotoTraceCalibrationTests
{
    [Fact]
    public void UnequalPreviewRoundingUsesOrientedSourceAspectForCalibration()
    {
        var points=new List<Point2> { new(10,20),new(10,120) };var before=points.ToArray();
        var result=PhotoTraceCalibration.Convert(1024,256,4001,1001,new(10,20),new(110,20),100,points);
        var expectedY=100d*1001*1024/(256*4001);
        Assert.Equal(0,result[0].X);Assert.Equal(0,result[0].Y);
        Assert.Equal(expectedY,result[1].Y,10);Assert.True(result[1].Y>100);
        Assert.Equal(before,points);
    }
    [Fact]
    public void OrientedWidthHeightSwapUsesTheCorrespondingPreviewAxes()
    {
        var result=PhotoTraceCalibration.Convert(256,1024,1001,4001,new(20,10),new(20,110),100,
            [new(20,10),new(120,10)]);
        var expectedX=100d*1001*1024/(256*4001);
        Assert.Equal(expectedX,result[1].X,10);Assert.Equal(0,result[1].Y);
    }
    [Fact]
    public void EqualDimensionsPreserveTheOriginalOverloadAndReferenceThreshold()
    {
        Point2[] points=[new(0,0),new(60,40),new(70,65)];
        var oldResult=PhotoTraceCalibration.Convert(100,100,new(10,10),new(20,20),250,points);
        var sameResult=PhotoTraceCalibration.Convert(100,100,100,100,new(10,10),new(20,20),250,points);
        Assert.Equal(oldResult,sameResult);
        // Minimum separation is in visible preview pixels, not enlarged source pixels.
        Assert.Throws<ArgumentException>(()=>PhotoTraceCalibration.Convert(100,100,400,400,new(1,1),new(1.5,1),10,points));
    }
    [Theory]
    [InlineData(0,1,1,1)] [InlineData(1,0,1,1)] [InlineData(1,1,0,1)] [InlineData(1,1,1,0)]
    [InlineData(12001,1,12000,1)] [InlineData(1,1,12001,1)] [InlineData(1,1,6000,6000)]
    [InlineData(4,2,3,2)] [InlineData(2,4,2,3)]
    public void InvalidPreviewAndSourceDimensionsAreRejected(int previewWidth,int previewHeight,int sourceWidth,int sourceHeight)
        =>Assert.Throws<ArgumentOutOfRangeException>(()=>PhotoTraceCalibration.Convert(previewWidth,previewHeight,sourceWidth,sourceHeight,new(0,0),new(1,0),10,[new(0,0),new(1,0)]));
    [Fact]
    public void DiagonalReferenceScalesAroundFirstPointAndKeepsDownwardY()
    {
        var input=new List<Point2> { new(15,20),new(25,45),new(23,34) };var original=input.ToArray();
        var result=PhotoTraceCalibration.Convert(100,100,new(20,30),new(23,34),50,input);
        Assert.Equal(new Point2(-50,-100),result[0]);Assert.Equal(new Point2(50,150),result[1]);Assert.Equal(new Point2(30,40),result[2]);
        Assert.Equal(original,input);input[0]=new(0,0);Assert.Equal(new Point2(-50,-100),result[0]);
        Assert.Throws<NotSupportedException>(()=>((IList<Point2>)result)[0]=new(1,1));
    }
    [Fact]
    public void ReferenceUsesPixelCentresAndIncludesEdgesAndSubpixelCoordinates()
    {
        var result=PhotoTraceCalibration.Convert(3,2,new(0,0),new(2,0),20,[new(0,0),new(2,1),new(0.5,0.5)]);
        Assert.Equal(new Point2(0,0),result[0]);Assert.Equal(new Point2(20,10),result[1]);Assert.Equal(new Point2(5,5),result[2]);
        var oneWide=PhotoTraceCalibration.Convert(1,2,new(0,0),new(0,1),10,[new(0,0),new(0,1)]);
        Assert.Equal(new Point2(0,10),oneWide[1]);
    }
    [Theory]
    [InlineData(0,100)] [InlineData(100,0)] [InlineData(12001,1)] [InlineData(1,12001)] [InlineData(6000,6000)]
    public void InvalidImageDimensionsAreRejected(int width,int height)
        =>Assert.Throws<ArgumentOutOfRangeException>(()=>PhotoTraceCalibration.Convert(width,height,new(0,0),new(1,0),1,[new(0,0),new(1,0)]));
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(100000001)]
    public void InvalidKnownLengthsAreRejected(double length)
        =>Assert.Throws<ArgumentOutOfRangeException>(()=>PhotoTraceCalibration.Convert(10,10,new(0,0),new(1,0),length,[new(0,0),new(1,0)]));
    [Theory]
    [InlineData(-0.01,0)] [InlineData(0,-0.01)] [InlineData(10,0)] [InlineData(0,10)] [InlineData(double.NaN,0)] [InlineData(0,double.PositiveInfinity)]
    public void OffImageOrNonfinitePixelIsRejectedForCalibrationAndTrace(double x,double y)
    {
        var point=new Point2(x,y);
        Assert.Throws<ArgumentOutOfRangeException>(()=>PhotoTraceCalibration.Convert(10,10,point,new(1,0),1,[new(0,0),new(1,0)]));
        Assert.Throws<ArgumentOutOfRangeException>(()=>PhotoTraceCalibration.Convert(10,10,new(0,0),point,1,[new(0,0),new(1,0)]));
        Assert.Throws<ArgumentOutOfRangeException>(()=>PhotoTraceCalibration.Convert(10,10,new(0,0),new(1,0),1,[new(0,0),point]));
    }
    [Fact]
    public void ZeroAndSubpixelReferenceAreRejectedButOnePixelIsAllowed()
    {
        Assert.Throws<ArgumentException>(()=>PhotoTraceCalibration.Convert(10,10,new(1,1),new(1,1),10,[new(0,0),new(1,1)]));
        Assert.Throws<ArgumentException>(()=>PhotoTraceCalibration.Convert(10,10,new(1,1),new(1.9,1),10,[new(0,0),new(1,1)]));
        Assert.Equal(new Point2(10,0),PhotoTraceCalibration.Convert(10,10,new(1,1),new(2,1),10,[new(1,1),new(2,1)])[1]);
    }
    [Fact]
    public void OutputCoordinateBoundsAndCollapsedSegmentsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>PhotoTraceCalibration.Convert(100,100,new(0,0),new(1,0),Geometry.MaxCoordinate,[new(0,0),new(2,0)]));
        Assert.Throws<ArgumentException>(()=>PhotoTraceCalibration.Convert(10,10,new(0,0),new(1,0),1,[new(1,1),new(1,1)]));
        Assert.Throws<ArgumentException>(()=>PhotoTraceCalibration.Convert(10,10,new(0,0),new(1,0),1e-10,[new(1,1),new(2,1)]));
        Assert.Throws<ArgumentOutOfRangeException>(()=>PhotoTraceCalibration.Convert(12000,1,new(0,0),new(11999,0),double.Epsilon,[new(0,0),new(1,0)]));
    }
    [Fact]
    public void CountBoundsAreEnforcedWithoutModifyingInput()
    {
        Assert.Throws<ArgumentNullException>(()=>PhotoTraceCalibration.Convert(10,10,new(0,0),new(1,0),1,null!));
        Assert.Throws<ArgumentOutOfRangeException>(()=>PhotoTraceCalibration.Convert(10,10,new(0,0),new(1,0),1,[new(0,0)]));
        var tooMany=Enumerable.Range(0,PhotoTraceCalibration.MaximumTracePoints+1).Select(i=>new Point2(i%2,0)).ToArray();var before=tooMany.ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(()=>PhotoTraceCalibration.Convert(10,10,new(0,0),new(1,0),1,tooMany));Assert.Equal(before,tooMany);
        var maximum=Enumerable.Range(0,PhotoTraceCalibration.MaximumTracePoints).Select(i=>new Point2(i%2,0)).ToArray();
        Assert.Equal(PhotoTraceCalibration.MaximumTracePoints,PhotoTraceCalibration.Convert(10,10,new(0,0),new(1,0),1,maximum).Count);
    }
}
