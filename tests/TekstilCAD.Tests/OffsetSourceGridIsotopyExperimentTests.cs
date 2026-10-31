using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public class OffsetSourceGridIsotopyExperimentTests
{
    private static bool Verify(Point2[] source, GridPoint[] grid)
    {
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetSourceGridIsotopyExperiment", true)!
            .GetMethod("TryVerify", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (bool)method.Invoke(null, [source, grid, CancellationToken.None])!;
    }
    private static GridPoint[] Quantize(Point2[] source) => source.Select(p => new GridPoint(
        (long)Math.Round(p.X * 1_000_000, MidpointRounding.AwayFromZero),
        (long)Math.Round(p.Y * 1_000_000, MidpointRounding.AwayFromZero))).ToArray();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThreeLobesHaveSimpleSourceToGridMotion(bool reverse)
    {
        var source = Enumerable.Range(0, 512).Select(i => {
            double a = 2 * Math.PI * i / 512, r = 1000 + 200 * Math.Cos(3 * a);
            return new Point2(r * Math.Cos(a), r * Math.Sin(a));
        }).ToArray();
        if (reverse) Array.Reverse(source);
        Assert.True(Verify(source, Quantize(source)));
    }

    [Fact]
    public void QuantizationThatFlattensSourceNotchIsRejected()
    {
        Point2[] source = [new(0,0), new(100,0), new(100,100), new(60,100),
            new(50,99.9999996), new(40,100), new(0,100)];
        Assert.False(Verify(source, Quantize(source)));
    }

    [Fact]
    public void MotionThatCollapsesTriangleIsRejected()
    {
        Point2[] source = [new(0,0), new(2,0), new(0,2)];
        GridPoint[] grid = [new(0,0), new(0,2_000_000), new(2_000_000,0)];
        Assert.False(Verify(source, grid));
    }
}
