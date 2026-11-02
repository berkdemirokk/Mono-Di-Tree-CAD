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
}
