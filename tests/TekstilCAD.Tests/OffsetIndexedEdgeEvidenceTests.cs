using System.Reflection;
using System.Runtime.ExceptionServices;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public class OffsetIndexedEdgeEvidenceTests
{
    private static (bool Ok,string Reason) Check(IReadOnlyList<GridPoint> source,IReadOnlyList<GridPoint> candidate,long delta,long error,CancellationToken token=default)
    {
        var method=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetIndexedEdgeEvidence",true)!.GetMethod("TryVerify",BindingFlags.Static|BindingFlags.NonPublic)!;
        object?[] args=[source,candidate,delta,error,null,token];
        try { return ((bool)method.Invoke(null,args)!, (string)args[4]!); }
        catch(TargetInvocationException ex) when(ex.InnerException is not null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw();throw; }
    }
    private static GridPoint[] Square(long offset=0)=>[new(0-offset,0-offset),new(100+offset,0-offset),new(100+offset,100+offset),new(0-offset,100+offset)];
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExactOutwardSquareWorksForBothOrientations(bool reverse)
    {
        var source=Square();var candidate=Square(10);
        if(reverse) { Array.Reverse(source);Array.Reverse(candidate); }
        var before=source.ToArray();var result=Check(source,candidate,10,0);
        Assert.True(result.Ok,result.Reason);Assert.Contains("unverified",result.Reason);Assert.Equal(before,source);
    }
    [Fact]
    public void WrongSideDistanceAndReversedIndexedEdgeAreRejected()
    {
        var source=Square();Assert.False(Check(source,Square(-10),10,0).Ok);
        var bad=Square(10);bad[0]=new(-11,-10);Assert.False(Check(source,bad,10,0).Ok);
        bad=Square(10);(bad[0],bad[1])=(bad[1],bad[0]);Assert.False(Check(source,bad,10,1).Ok);
    }
    [Fact]
    public void CallerSelectedBandIsExactAtInclusiveBoundary()
    {
        var source=Square();Assert.True(Check(source,Square(11),10,1).Ok);
        Assert.False(Check(source,Square(12),10,1).Ok);
        Assert.False(Check(source,Square(10),10,10).Ok);
    }
    [Fact]
    public void ExactIntegerArithmeticHandlesLargeOriginsWithoutOverflow()
    {
        const long origin=9_000_000_000_000_000_000;
        GridPoint Move(GridPoint p)=>new(origin+p.X,origin+p.Y);
        Assert.True(Check(Square().Select(Move).ToArray(),Square(10).Select(Move).ToArray(),10,0).Ok);
    }
    [Fact]
    public void BadCountsZeroEdgesAndCancellationAreRejected()
    {
        var source=Square();var bad=Square(10);bad[1]=bad[0];
        Assert.False(Check(source,bad,10,1).Ok);Assert.False(Check(source,bad.Take(3).ToArray(),10,1).Ok);
        using var c=new CancellationTokenSource();c.Cancel();Assert.ThrowsAny<OperationCanceledException>(()=>Check(source,bad,10,1,c.Token));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LobedIdealCandidateHasIndependentPerEdgeDistanceEvidence(bool reverse)
    {
        const long grid=1_000_000;
        var source=Enumerable.Range(0,512).Select(i=>
        {
            var a=Math.Tau*i/512;var r=1000+200*Math.Cos(3*a);
            return new GridPoint((long)Math.Round(r*Math.Cos(a)*grid,MidpointRounding.AwayFromZero),
                (long)Math.Round(r*Math.Sin(a)*grid,MidpointRounding.AwayFromZero));
        }).ToArray();
        if(reverse)Array.Reverse(source);
        var method=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof",true)!.GetMethod("TryCreate",BindingFlags.Static|BindingFlags.NonPublic)!;
        object?[] args=[source,grid,null,null,CancellationToken.None];
        Assert.True((bool)method.Invoke(null,args)!, (string?)args[3]);
        var candidate=Assert.IsAssignableFrom<IReadOnlyList<GridPoint>>(args[2]);
        var evidence=Check(source,candidate,grid,1);
        Assert.True(evidence.Ok,evidence.Reason);
        // This checks only the constructed ideal candidate; it does not compare
        // with Clipper's forward boundary or authorize production use.
    }
}
