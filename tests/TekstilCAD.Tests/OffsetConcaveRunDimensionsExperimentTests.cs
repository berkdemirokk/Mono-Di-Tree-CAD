using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public class OffsetConcaveRunDimensionsExperimentTests
{
    private static bool Verify(Point2[] source, GridPoint[] candidate)
    {
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetConcaveRunDimensionsExperiment", true)!
            .GetMethod("TryVerify", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (bool)method.Invoke(null, [source, candidate, CancellationToken.None])!;
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DenseLobesRetainEveryConcaveRunSubchainDimensions(bool reverse)
    {
        var source = Enumerable.Range(0,512).Select(i => {
            double a=2*Math.PI*i/512, r=1000+200*Math.Cos(3*a);
            return new Point2(r*Math.Cos(a),r*Math.Sin(a));
        }).ToArray();
        if (reverse) Array.Reverse(source);
        var grid = source.Select(p => new GridPoint((long)Math.Round(p.X*1_000_000,MidpointRounding.AwayFromZero),
            (long)Math.Round(p.Y*1_000_000,MidpointRounding.AwayFromZero))).ToArray();
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetIndexedMiterPrototype", true)!
            .GetMethod("TryConstruct", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] args = [grid, 1_000_000L, null, CancellationToken.None];
        Assert.True((bool)method.Invoke(null,args)!);
        Assert.True(Verify(source, (GridPoint[])args[2]!));
    }
    [Fact]
    public void LostInnerNotchIsRejected()
    {
        Point2[] source = [new(0,0),new(100,0),new(100,100),new(60,100),new(56,98),
            new(52,98),new(50,97),new(48,98),new(44,98),new(40,100),new(0,100)];
        var candidate=source.Select(p => new GridPoint((long)(p.X*1_000_000),(long)(p.Y*1_000_000))).ToArray();
        candidate[6]=new(50_000_000,98_000_000);
        Assert.False(Verify(source,candidate));
    }
}
