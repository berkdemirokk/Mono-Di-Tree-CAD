using System.Reflection;
using Clipper2Lib;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetChordComparisonDiagnosticTests
{
    private static (bool Ok, string Reason) Compare(GridPoint[] a, GridPoint[] b, long p = 1, long q = 1)
    {
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetChordComparisonDiagnostic", true)!
            .GetMethod("TryCompare", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] args = [a, b, p, q, null, CancellationToken.None];
        var ok = (bool)method.Invoke(null, args)!;
        return (ok, (string)args[4]!);
    }
    private static GridPoint[] Small(long depth) =>
        [new(0,0),new(20,0),new(20,20),new(12,20),new(10,20-depth),new(8,20),new(0,20)];

    [Fact]
    public void ExactRatioDetectsReducedDepthAndAllowsEquality()
    {
        var source = Small(2); var candidate = Small(1);
        Assert.False(Compare(source,candidate).Ok);
        Assert.True(Compare(source,candidate,1,2).Ok);
        Assert.False(Compare(source,candidate,2,3).Ok);
    }

    [Fact]
    public void LostSmallRecessFailsClosed()
    {
        var candidate = Small(1); candidate[4] = new(10,20);
        Assert.False(Compare(Small(1), candidate).Ok);
    }

    [Fact]
    public void LostNestedRecessFailsClosed()
    {
        GridPoint[] source = [new(0,0),new(12,0),new(12,12),new(10,12),new(9,10),new(8,11),
            new(6,8),new(4,11),new(3,10),new(2,12),new(0,12)];
        var candidate = (GridPoint[])source.Clone(); candidate[4] = new(9,11);
        Assert.False(Compare(source,candidate).Ok);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualLobedForwardOutputCannotBeClaimedCertified(bool reverse)
    {
        var source = Enumerable.Range(0,512).Select(i => { var a = Math.Tau*i/512; var r = 1000+200*Math.Cos(3*a);
            return new GridPoint((long)Math.Round(r*Math.Cos(a)*1_000_000,MidpointRounding.AwayFromZero),
                (long)Math.Round(r*Math.Sin(a)*1_000_000,MidpointRounding.AwayFromZero)); }).ToArray();
        if (reverse) Array.Reverse(source);
        var path = new Path64(source.Select(p => new Point64(p.X,p.Y)));
        var forward = Assert.Single(Clipper.InflatePaths(new Paths64 { path }, 1_000_000, JoinType.Miter, EndType.Polygon, 2.0));
        var candidate = forward.Select(p => new GridPoint(p.X,p.Y)).ToArray();
        var result = Compare(source,candidate);
        Assert.False(result.Ok, "The candidate has no established indexed correspondence or certificate.");
    }
}
