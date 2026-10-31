using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public class OffsetSourceVertexDepthExperimentTests
{
    private static bool Verify(Point2[] source, GridPoint[] candidate)
    {
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetSourceVertexDepthExperiment", true)!
            .GetMethod("TryVerify", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (bool)method.Invoke(null, [source, candidate, CancellationToken.None])!;
    }

    private static bool Construct(GridPoint[] source, out GridPoint[] result)
    {
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetIndexedMiterPrototype", true)!
            .GetMethod("TryConstruct", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] args = [source, 1_000_000L, null, CancellationToken.None];
        bool success = (bool)method.Invoke(null, args)!;
        result = (GridPoint[])args[2]!;
        return success;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThreeLobesRetainAtLeastHalfOfEverySourceVertexDepth(bool reverse)
    {
        var source = Enumerable.Range(0, 512).Select(i => {
            double a = 2 * Math.PI * i / 512, r = 1000 + 200 * Math.Cos(3 * a);
            return new Point2(r * Math.Cos(a), r * Math.Sin(a));
        }).ToArray();
        if (reverse) Array.Reverse(source);
        var grid = source.Select(p => new GridPoint((long)Math.Round(p.X * 1_000_000, MidpointRounding.AwayFromZero),
            (long)Math.Round(p.Y * 1_000_000, MidpointRounding.AwayFromZero))).ToArray();
        Assert.True(Construct(grid, out var candidate));
        Assert.True(Verify(source, candidate));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceNotchErasedByGridRoundingIsRejected(bool reverse)
    {
        Point2[] source = [new(0,0), new(100,0), new(100,100), new(60,100),
            new(50,99.9999996), new(40,100), new(0,100)];
        var grid = source.Select(p => new GridPoint((long)Math.Round(p.X * 1_000_000, MidpointRounding.AwayFromZero),
            (long)Math.Round(p.Y * 1_000_000, MidpointRounding.AwayFromZero))).ToArray();
        if (reverse) { Array.Reverse(source); Array.Reverse(grid); }
        Assert.True(Construct(grid, out var candidate));
        Assert.False(Verify(source, candidate));
    }
}
