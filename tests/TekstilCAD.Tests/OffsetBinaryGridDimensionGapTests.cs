using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Counterexample: matching grid turns does not bound source dimensions.</summary>
public sealed class OffsetBinaryGridDimensionGapTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DifferentBinary64SquareWidthsCanShareTheSameGridAndCandidate(bool reverse)
    {
        const double inset=0.00000049;
        Point2[] outer=[new(0,0),new(100,0),new(100,100),new(0,100)];
        Point2[] inner=[new(inset,inset),new(100-inset,inset),new(100-inset,100-inset),new(inset,100-inset)];
        if(reverse){Array.Reverse(outer);Array.Reverse(inner);}
        Geometry.ValidateContour(outer);Geometry.ValidateContour(inner);
        static GridPoint[] Grid(Point2[] p)=>p.Select(v=>new GridPoint(
            (long)Math.Round(v.X*1_000_000,MidpointRounding.AwayFromZero),
            (long)Math.Round(v.Y*1_000_000,MidpointRounding.AwayFromZero))).ToArray();
        var a=Grid(outer);var b=Grid(inner);
        Assert.Equal(a,b);
        var guard=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetSourceGridTurnGuard",true)!
            .GetMethod("PreservesTurns",BindingFlags.Static|BindingFlags.NonPublic)!;
        Assert.True((bool)guard.Invoke(null,[outer,a.Select(v=>(v.X,v.Y)).ToArray(),CancellationToken.None])!);
        Assert.True((bool)guard.Invoke(null,[inner,b.Select(v=>(v.X,v.Y)).ToArray(),CancellationToken.None])!);
        var certificate=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetIndexedSweepCertificate",true)!
            .GetMethod("TryCreate",BindingFlags.Static|BindingFlags.NonPublic)!;
        object?[] argsA=[a,1_000_000L,null,null,CancellationToken.None];
        object?[] argsB=[b,1_000_000L,null,null,CancellationToken.None];
        Assert.True((bool)certificate.Invoke(null,argsA)!, (string?)argsA[3]);
        Assert.True((bool)certificate.Invoke(null,argsB)!, (string?)argsB[3]);
        Assert.Equal((IReadOnlyList<GridPoint>)argsA[2]!, (IReadOnlyList<GridPoint>)argsB[2]!);
        var widthDifference=Geometry.Bounds(outer).Width-Geometry.Bounds(inner).Width;
        Assert.InRange(widthDifference,0.00000097999,0.00000098001);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DifferentSourceHeightsCanShareTheSameGridAndTurnSigns(bool reverse)
    {
        Point2[] deeper=[new(0,0),new(100,0),new(100,100),new(50,100-0.00001049),new(0,100)];
        Point2[] shallower=[new(0,0),new(100,0),new(100,100),new(50,100-0.00000951),new(0,100)];
        if(reverse){Array.Reverse(deeper);Array.Reverse(shallower);}
        Geometry.ValidateContour(deeper);Geometry.ValidateContour(shallower);
        static (long X,long Y)[] Grid(Point2[] p)=>p.Select(v=>(
            (long)Math.Round(v.X*1_000_000,MidpointRounding.AwayFromZero),
            (long)Math.Round(v.Y*1_000_000,MidpointRounding.AwayFromZero))).ToArray();
        var a=Grid(deeper);var b=Grid(shallower);
        Assert.Equal(a,b);
        var guard=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetSourceGridTurnGuard",true)!
            .GetMethod("PreservesTurns",BindingFlags.Static|BindingFlags.NonPublic)!;
        Assert.True((bool)guard.Invoke(null,[deeper,a,CancellationToken.None])!);
        Assert.True((bool)guard.Invoke(null,[shallower,b,CancellationToken.None])!);
        int witness=reverse ? 1 : 3;
        Assert.InRange(shallower[witness].Y-deeper[witness].Y,0.00000097999,0.00000098001);
    }
}
