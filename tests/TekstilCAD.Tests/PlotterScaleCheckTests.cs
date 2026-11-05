using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public sealed class PlotterScaleCheckTests
{
    [Fact]
    public void ReportsEachAxisAgainstTheReferenceWithoutAssumingTolerance()
    {
        var result = PlotterScaleCheck.Compare(99.5, 100.25);
        Assert.Equal(-0.5, result.XDeviationMillimeters);
        Assert.Equal(0.25, result.YDeviationMillimeters);
        Assert.Equal(new PlotterScaleCheck.Result(0, 0), PlotterScaleCheck.Compare(100, 100));
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(1001)] [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidMeasurementsOnEitherAxis(double invalid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlotterScaleCheck.Compare(invalid, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlotterScaleCheck.Compare(100, invalid));
    }

    [Fact]
    public void ComparesDiagonalsIndependentlyWithoutAssumingTolerance()
    {
        var nominal=100*Math.Sqrt(2);
        var exact=PlotterScaleCheck.CompareDiagonals(nominal,nominal);
        Assert.Equal(0,exact.FirstDeviationMillimeters);
        Assert.Equal(0,exact.SecondDeviationMillimeters);
        Assert.Equal(0,exact.DifferenceMillimeters);
        var result=PlotterScaleCheck.CompareDiagonals(nominal+1,nominal-2);
        Assert.Equal(1,result.FirstDeviationMillimeters,10);
        Assert.Equal(-2,result.SecondDeviationMillimeters,10);
        Assert.Equal(3,result.DifferenceMillimeters,10);
        Assert.Equal(-3,PlotterScaleCheck.CompareDiagonals(nominal-2,nominal+1).DifferenceMillimeters,10);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(1501)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidDiagonalMeasurements(double invalid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>PlotterScaleCheck.CompareDiagonals(invalid,100));
        Assert.Throws<ArgumentOutOfRangeException>(()=>PlotterScaleCheck.CompareDiagonals(100,invalid));
    }
}
