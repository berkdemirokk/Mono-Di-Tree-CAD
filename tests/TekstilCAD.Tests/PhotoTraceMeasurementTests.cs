using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public class PhotoTraceMeasurementTests
{
    [Fact]
    public void PolylineUsesEveryEdgeAndIsIndependentOfDirection()
    {
        Point2[] trace=[new(20,20),new(50,20),new(50,60)];
        var forward=PhotoTraceMeasurement.ComparePolyline(100,100,100,100,new(0,0),new(10,0),10,trace,65);
        var reversed=PhotoTraceMeasurement.ComparePolyline(100,100,100,100,new(0,0),new(10,0),10,trace.Reverse().ToArray(),65);
        Assert.Equal(70,forward.EstimatedMm,8);Assert.Equal(5,forward.DifferenceMm,8);Assert.Equal(forward,reversed);
    }
    [Fact]
    public void PlanePolylineUsesCorrectedEdgesAndRejectsInvalidInputs()
    {
        var plane=new PhotoPlaneCalibration { Corners=[new(10,10),new(90,10),new(90,90),new(10,90)],WidthMm=200,HeightMm=100 };
        Point2[] trace=[new(20,20),new(80,20),new(80,80)];
        var measured=PhotoTraceMeasurement.ComparePlanePolyline(100,100,plane,trace,225);
        Assert.Equal(225,measured.EstimatedMm,8);Assert.Equal(0,measured.DifferenceMm,8);
        Assert.Throws<ArgumentException>(()=>PhotoTraceMeasurement.ComparePlanePolyline(100,100,plane,[new(20,20),new(20,20)],10));
        Assert.Throws<ArgumentOutOfRangeException>(()=>PhotoTraceMeasurement.ComparePlanePolyline(100,100,plane,trace,0));
        Assert.Throws<ArgumentException>(()=>PhotoTraceMeasurement.ComparePlanePolyline(100,100,plane,[new(10,10),new(90,10)],100));
    }
    [Fact]
    public void SeparateMeasurementUsesSameSourceAspectAsPhotoTrace()
    {
        var value=PhotoTraceMeasurement.Compare(1024,256,4001,1001,new(10,20),new(110,20),100,new(10,20),new(10,120),102);
        var expected=100d*1001*1024/(256*4001);
        Assert.Equal(expected,value.EstimatedMm,10);Assert.Equal(102,value.KnownMm);Assert.Equal(expected-102,value.DifferenceMm,10);
    }
    [Fact]
    public void SameCalibrationSegmentIsNotAnIndependentCheck()
    {
        Assert.Throws<ArgumentException>(()=>PhotoTraceMeasurement.Compare(100,100,100,100,new(10,10),new(20,10),100,new(10,10),new(20,10),100));
        Assert.Throws<ArgumentException>(()=>PhotoTraceMeasurement.Compare(100,100,100,100,new(10,10),new(20,10),100,new(20,10),new(10,10),100));
    }
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(100000001)]
    public void InvalidKnownLengthsAreRejected(double known)
        =>Assert.Throws<ArgumentOutOfRangeException>(()=>PhotoTraceMeasurement.Compare(100,100,100,100,new(10,10),new(20,10),100,new(10,10),new(10,20),known));
    [Fact]
    public void InvalidCoordinatesAndCollapsedCheckSegmentAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>PhotoTraceMeasurement.Compare(100,100,100,100,new(10,10),new(20,10),100,new(-1,0),new(10,20),100));
        Assert.Throws<ArgumentException>(()=>PhotoTraceMeasurement.Compare(100,100,100,100,new(10,10),new(20,10),100,new(10,20),new(10,20),100));
    }
}
