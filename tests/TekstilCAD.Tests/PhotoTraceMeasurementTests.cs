using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public class PhotoTraceMeasurementTests
{
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
