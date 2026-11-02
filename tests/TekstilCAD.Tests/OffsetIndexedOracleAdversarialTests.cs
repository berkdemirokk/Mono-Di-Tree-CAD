using System.Numerics;
using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Exact determinant oracle and fail-closed examples; not production approval.</summary>
public sealed class OffsetIndexedOracleAdversarialTests
{
    [Theory]
    [InlineData(false, false, 0L)]
    [InlineData(true, false, 50000000000000L)]
    [InlineData(false, true, -50000000000000L)]
    [InlineData(true, true, 0L)]
    public void DirectedIntervalsEncloseIndependentExactDeterminantSolution(bool reverse,bool nearParallel,long origin)
    {
        long ux=nearParallel?1_000_001:3, uy=nearParallel?0:4;
        long vx=nearParallel?999_999:5, vy=nearParallel?2000:12;
        long lu=nearParallel?1_000_001:5, lv=nearParallel?1_000_001:13;
        const long delta=130;
        var a=new GridPoint(origin-ux,origin-uy);var b=new GridPoint(origin,origin);var c=new GridPoint(origin+vx,origin+vy);
        // Independent linear system for shifted edge lines; rational coordinates
        // are kept as integer numerators over a positive determinant.
        BigInteger det=(BigInteger)ux*vy-(BigInteger)uy*vx;
        BigInteger xn=(BigInteger)origin*det+(BigInteger)delta*(ux*lv-vx*lu);
        BigInteger yn=(BigInteger)origin*det+(BigInteger)delta*(uy*lv-vy*lu);
        if(reverse)(a,c)=(c,a);
        Assert.True(TryBound(a,b,c,delta,reverse?-1:1,out var box));
        Assert.True(box.X.Lower*det<=xn*Scale && xn*Scale<=box.X.Upper*det);
        Assert.True(box.Y.Lower*det<=yn*Scale && yn*Scale<=box.Y.Upper*det);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualIndexedOutputMatchesExact345RotatedSquare(bool reverse)
    {
        static GridPoint Rotate(long x,long y) => new(3*x-4*y,4*x+3*y);
        GridPoint[] source=[Rotate(0,0),Rotate(1000,0),Rotate(1000,1000),Rotate(0,1000)];
        GridPoint[] expected=[Rotate(-100,-100),Rotate(1100,-100),Rotate(1100,1100),Rotate(-100,1100)];
        if(reverse){Array.Reverse(source);Array.Reverse(expected);}
        var result=Candidate(source,500);
        Assert.True(result.Ok,result.Reason);
        Assert.Equal(expected,result.Points);
    }

    [Theory]
    [InlineData(false, 0L)]
    [InlineData(true, 0L)]
    [InlineData(false, 50000000000000L)]
    [InlineData(true, -50000000000000L)]
    public void QuarterTurnOneGridNotchIsNeverCertified(bool reverse,long origin)
    {
        const long u=1_000_000;
        GridPoint[] original=[new(0,0),new(100*u,0),new(100*u,100*u),new(50*u+2,100*u),
            new(50*u+2,100*u-1),new(50*u,100*u-1),new(50*u,100*u),new(0,100*u)];
        var source=original.Select(p=>new GridPoint(origin-p.Y,origin+p.X)).ToArray();
        if(reverse)Array.Reverse(source);
        var before=source.ToArray();
        var result=Candidate(source,u);
        Assert.False(result.Ok,result.Reason);
        Assert.Empty(result.Points);
        Assert.Equal(before,source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcuteMiterAndClosedNarrowChannelFailClosed(bool reverse)
    {
        const long u=1_000_000;
        GridPoint[] acute=[new(0,0),new(100*u,0),new(100*u,u)];
        GridPoint[] channel=[new(0,0),new(100*u,0),new(100*u,100*u),new(52*u,100*u),
            new(52*u,20*u),new(48*u,20*u),new(48*u,100*u),new(0,100*u)];
        if(reverse){Array.Reverse(acute);Array.Reverse(channel);}
        var acuteResult=Candidate(acute,u);
        Assert.False(acuteResult.Ok);
        Assert.Contains("Miter",acuteResult.Reason);
        Assert.False(Candidate(channel,5*u).Ok);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuantizationCollidingSourceVerticesNeverMutateOriginal(bool reverse)
    {
        var points=new List<Point2> {new(0,0),new(100,0),new(100,100),new(50.0000004,100),new(50,100),new(0,100)};
        if(reverse)points.Reverse();
        var before=points.ToArray();
        Geometry.ValidateContour(points);
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.Offset(new PatternPiece {Closed=true,Points=points},1));
        Assert.Equal(before,points);
    }

    private static (bool Ok,IReadOnlyList<GridPoint> Points,string Reason) Candidate(GridPoint[] points,long delta)
    {
        var method=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof",true)!
            .GetMethod("TryCreate",BindingFlags.Static|BindingFlags.NonPublic)!;
        object?[] args=[points,delta,null,null,CancellationToken.None];
        var ok=(bool)method.Invoke(null,args)!;
        return (ok,(IReadOnlyList<GridPoint>)args[2]!, (string)args[3]!);
    }
}
