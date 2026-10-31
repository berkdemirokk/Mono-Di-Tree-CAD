using System.Collections;
using System.Numerics;
using System.Reflection;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetChordRecessDiagnosticTests
{
    private sealed record Recess(int Start, int End, BigInteger Width2, BigInteger DepthNum, BigInteger DepthDen);
    private static (bool Ok, Recess[] Recesses, string Reason) Measure(GridPoint[] source)
    {
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetChordRecessDiagnostic", true)!
            .GetMethod("TryMeasure", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] args = [source, null, null, CancellationToken.None];
        bool ok = (bool)method.Invoke(null, args)!;
        var items = ((IEnumerable)args[1]!).Cast<object>().Select(x => {
            var t = x.GetType();
            return new Recess((int)t.GetProperty("Start")!.GetValue(x)!, (int)t.GetProperty("End")!.GetValue(x)!,
                (BigInteger)t.GetProperty("WidthSquared")!.GetValue(x)!,
                (BigInteger)t.GetProperty("DepthNumeratorSquared")!.GetValue(x)!,
                (BigInteger)t.GetProperty("DepthDenominator")!.GetValue(x)!);
        }).ToArray();
        return (ok, items, (string)args[2]!);
    }
    private static GridPoint[] Small() => [new(0,0),new(10,0),new(10,10),new(6,10),new(5,9),new(4,10),new(0,10)];
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactSmallRecessDimensionsAndWinding(bool reverse)
    {
        var p=Small(); if(reverse) Array.Reverse(p);
        var result=Measure(p); Assert.True(result.Ok,result.Reason);
        int a=Array.IndexOf(p,new GridPoint(6,10)), b=Array.IndexOf(p,new GridPoint(4,10));
        var r=Assert.Single(result.Recesses, x => x.Start==(reverse?b:a) && x.End==(reverse?a:b));
        Assert.Equal(new BigInteger(4),r.Width2);
        Assert.Equal(new BigInteger(4),r.DepthNum);
        Assert.Equal(new BigInteger(4),r.DepthDen);
    }
    [Fact]
    public void FlattenedLostRecessDoesNotYieldOriginalWitness()
    {
        var source=Measure(Small()); Assert.True(source.Ok,source.Reason);
        GridPoint[] flat=[new(0,0),new(10,0),new(10,10),new(0,10)];
        var lost=Measure(flat); Assert.True(lost.Ok,lost.Reason);
        Assert.NotEmpty(source.Recesses); Assert.Empty(lost.Recesses);
    }
    [Fact]
    public void AdjacentAndNestedChainsAreEnumeratedIndependently()
    {
        GridPoint[] p=[new(0,0),new(12,0),new(12,12),new(10,12),new(9,10),new(8,11),
            new(6,8),new(4,11),new(3,10),new(2,12),new(0,12)];
        var result=Measure(p); Assert.True(result.Ok,result.Reason);
        Assert.Contains(result.Recesses, x => x.Start==3 && x.End==5);
        Assert.Contains(result.Recesses, x => x.Start==7 && x.End==9);
        Assert.Contains(result.Recesses, x => x.Start==3 && x.End==9);
    }
    [Fact]
    public void InvalidContourFailsClosedWithoutPartialMeasurements()
    {
        GridPoint[] p=[new(0,0),new(10,10),new(0,10),new(10,0),new(12,4)];
        var result=Measure(p); Assert.False(result.Ok); Assert.Empty(result.Recesses);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LobedContourIsEitherFullyEnumeratedOrExplicitlyRejected(bool reverse)
    {
        var p=Enumerable.Range(0,512).Select(i => { double a=Math.Tau*i/512, r=1000+200*Math.Cos(3*a);
            return new GridPoint((long)Math.Round(r*Math.Cos(a)*1_000_000), (long)Math.Round(r*Math.Sin(a)*1_000_000)); }).ToArray();
        if(reverse) Array.Reverse(p);
        var result=Measure(p);
        if(result.Ok) Assert.NotEmpty(result.Recesses); else Assert.Empty(result.Recesses);
    }
}
