using System.Reflection;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetIndexedMiterPrototypeTests
{
    private static bool Construct(GridPoint[] source, long delta, out GridPoint[] result)
    {
        var type = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetIndexedMiterPrototype", true)!;
        var method = type.GetMethod("TryConstruct", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] args = [source, delta, null, CancellationToken.None];
        var ok = (bool)method.Invoke(null, args)!;
        result = (GridPoint[])args[2]!;
        return ok;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RectangleHasExactIndexedMiter(bool reverse)
    {
        GridPoint[] source = [new(0,0),new(100,0),new(100,100),new(0,100)];
        if (reverse) Array.Reverse(source);
        Assert.True(Construct(source, 5, out var actual));
        GridPoint[] expected = [new(-5,-5),new(105,-5),new(105,105),new(-5,105)];
        if (reverse) Array.Reverse(expected);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedNarrowRecessIsRejected(bool reverse)
    {
        GridPoint[] source = [new(0,0),new(100,0),new(100,100),new(52,100),new(52,20),new(48,20),new(48,100),new(0,100)];
        if (reverse) Array.Reverse(source);
        Assert.False(Construct(source, 5, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DenseThreeLobeCandidateRetainsEveryIndexedTurn(bool reverse)
    {
        const long scale = 1_000_000;
        var source = Enumerable.Range(0,512).Select(i => {
            double a = 2*Math.PI*i/512, radius = 1000+200*Math.Cos(3*a);
            return new GridPoint((long)Math.Round(scale*radius*Math.Cos(a),MidpointRounding.AwayFromZero),
                (long)Math.Round(scale*radius*Math.Sin(a),MidpointRounding.AwayFromZero));
        }).ToArray();
        if (reverse) Array.Reverse(source);
        var before = (GridPoint[])source.Clone();
        Assert.True(Construct(source, scale, out var candidate));
        Assert.Equal(source.Length, candidate.Length);
        Assert.Equal(before, source);
    }
}
