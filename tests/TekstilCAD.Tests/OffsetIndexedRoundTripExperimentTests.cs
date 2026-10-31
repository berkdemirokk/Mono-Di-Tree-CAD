using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public class OffsetIndexedRoundTripExperimentTests
{
    private static bool Construct(Point2[] source)
    {
        var grid = source.Select(p => new GridPoint((long)Math.Round(p.X*1_000_000, MidpointRounding.AwayFromZero),
            (long)Math.Round(p.Y*1_000_000, MidpointRounding.AwayFromZero))).ToArray();
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetIndexedRoundTripExperiment", true)!
            .GetMethod("TryConstruct", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [source, grid, 1_000_000L, null, CancellationToken.None];
        return (bool)method.Invoke(null, args)!;
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DenseLobesRoundTripExactly(bool reverse)
    {
        var source = Enumerable.Range(0, 512).Select(i => {
            double a = 2 * Math.PI * i / 512, r = 1000 + 200 * Math.Cos(3 * a);
            return new Point2(r * Math.Cos(a), r * Math.Sin(a));
        }).ToArray();
        if (reverse) Array.Reverse(source);
        Assert.True(Construct(source));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuantizationLostNotchCannotBeAccepted(bool reverse)
    {
        Point2[] source = [new(0,0), new(100,0), new(100,100), new(60,100),
            new(50,99.9999996), new(40,100), new(0,100)];
        if (reverse) Array.Reverse(source);
        Assert.False(Construct(source));
    }
}
