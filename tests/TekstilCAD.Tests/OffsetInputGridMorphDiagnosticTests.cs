using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetInputGridMorphDiagnosticTests
{
    private static (bool Ok,string Reason) Verify(Point2[] source, GridPoint[] grid)
    {
        var method=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetInputGridMorphDiagnostic",true)!
            .GetMethod("TryVerify",BindingFlags.Static|BindingFlags.NonPublic)!;
        object?[] args=[source,grid,null,CancellationToken.None];
        var ok=(bool)method.Invoke(null,args)!;
        return (ok,(string)args[2]!);
    }
    private static GridPoint[] Grid(Point2[] p) => p.Select(x => new GridPoint(
        checked((long)Math.Round(x.X*1_000_000,MidpointRounding.AwayFromZero)),
        checked((long)Math.Round(x.Y*1_000_000,MidpointRounding.AwayFromZero)))).ToArray();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LobedInputToActualGridMorph(bool reverse)
    {
        var source=Enumerable.Range(0,512).Select(i=>{
            double a=Math.Tau*i/512, r=1000+200*Math.Cos(3*a);
            return new Point2(r*Math.Cos(a),r*Math.Sin(a));
        }).ToArray();
        if(reverse) Array.Reverse(source);
        var result=Verify(source,Grid(source));
        Assert.True(result.Ok,result.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecessFlattenedByGridIsRejected(bool reverse)
    {
        Point2[] source=[new(0,0),new(10,0),new(10,10),new(6,10),
            new(5,9.9999996),new(4,10),new(0,10)];
        if(reverse) Array.Reverse(source);
        var result=Verify(source,Grid(source));
        Assert.False(result.Ok);
        Assert.Contains("Turn",result.Reason);
    }

    [Fact]
    public void NonfiniteInputIsRejected()
    {
        Point2[] source=[new(0,0),new(double.NaN,0),new(0,1)];
        GridPoint[] grid=[new(0,0),new(1,0),new(0,1)];
        Assert.False(Verify(source,grid).Ok);
    }
}
