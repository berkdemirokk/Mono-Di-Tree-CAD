using System.Reflection;
using TekstilCAD.Patterns;
using Xunit.Abstractions;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;
namespace TekstilCAD.Tests;

public class OffsetLinearMorphDiagnosticTests(ITestOutputHelper output)
{
    private static GridPoint[] Square(long offset=0)=>[new(-offset,-offset),new(100+offset,-offset),new(100+offset,100+offset),new(-offset,100+offset)];
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AnalyticSquareExpansionHasStrictCertificateInBothOrientations(bool reverse)
    {
        var source=Square();var target=Square(10);
        if(reverse) { Array.Reverse(source);Array.Reverse(target); }
        var before=source.ToArray();
        Assert.True(OffsetLinearMorphDiagnostic.TryVerify(source,target,out var reason),reason);
        Assert.Contains("unverified",reason);Assert.Equal(before,source);
    }
    [Fact]
    public void HalfTurnHasSimpleEndpointsButCollapsesAtMidpointAndIsRejected()
    {
        var source=Square();var target=source.Select(p=>new GridPoint(100-p.X,100-p.Y)).ToArray();
        Assert.False(OffsetLinearMorphDiagnostic.TryVerify(source,target,out var reason));Assert.Contains("Edge",reason);
    }
    [Fact]
    public void CollapsedNarrowRecessIsRejected()
    {
        GridPoint[] source=[new(0,0),new(100,0),new(100,100),new(52,100),new(52,20),new(48,20),new(48,100),new(0,100)];
        GridPoint[] target=[new(-5,-5),new(105,-5),new(105,105),new(50,105),new(50,15),new(50,15),new(50,105),new(-5,105)];
        Assert.False(OffsetLinearMorphDiagnostic.TryVerify(source,target,out _));
    }
    [Fact]
    public void AValidMorphAloneDoesNotProveOffsetOrPreserveDimensions()
    {
        GridPoint[] source=[new(0,0),new(10000,0),new(10000,10000),new(0,10000)];
        GridPoint[] tiny=[new(4999,4999),new(5001,4999),new(5001,5001),new(4999,5001)];
        Assert.True(OffsetLinearMorphDiagnostic.TryVerify(source,tiny,out var reason),reason);
        Assert.Contains("feature dimensions remain unverified",reason);
        // Even a proven simple continuous morph can shrink this shape almost to a point.
    }
    [Fact]
    public void FarOriginAndCancellationAreHandledExactly()
    {
        const long origin=9_000_000_000_000_000_000;
        GridPoint Move(GridPoint p)=>new(origin+p.X,-origin+p.Y);
        var source=Square().Select(Move).ToArray();var target=Square(10).Select(Move).ToArray();
        Assert.True(OffsetLinearMorphDiagnostic.TryVerify(source,target,out var reason),reason);
        using var c=new CancellationTokenSource();c.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>OffsetLinearMorphDiagnostic.TryVerify(source,target,out _,c.Token));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LobedAnalyticCandidateReceivesHonestMorphObservation(bool reverse)
    {
        const long grid=1_000_000;
        var source=Enumerable.Range(0,512).Select(i=>
        {
            var angle=Math.Tau*i/512;var radius=1000+200*Math.Cos(3*angle);
            return new GridPoint((long)Math.Round(radius*Math.Cos(angle)*grid,MidpointRounding.AwayFromZero),
                (long)Math.Round(radius*Math.Sin(angle)*grid,MidpointRounding.AwayFromZero));
        }).ToArray();
        if(reverse)Array.Reverse(source);
        var proof=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof",true)!;
        var create=proof.GetMethod("TryCreate",BindingFlags.NonPublic|BindingFlags.Static)!;
        object?[] args=[source,grid,null,null,CancellationToken.None];
        var created=(bool)create.Invoke(null,args)!;
        output.WriteLine($"candidate-created={created} reason={args[3]}");
        Assert.True(created,(string?)args[3]);
        var candidate=Assert.IsAssignableFrom<IReadOnlyList<GridPoint>>(args[2]);
        var result=OffsetLinearMorphDiagnostic.TryVerify(source,candidate,out var reason);
        output.WriteLine($"reverse={reverse}, linear-morph-proven={result}, reason={reason}");
        Assert.False(string.IsNullOrWhiteSpace(reason));
        // This proves or rejects only indexed morph topology; it never changes the production guard.
    }
}
