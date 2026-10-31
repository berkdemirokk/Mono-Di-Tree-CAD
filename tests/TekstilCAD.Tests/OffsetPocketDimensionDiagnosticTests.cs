using System.Numerics;
using System.Reflection;
using TekstilCAD.Patterns;
using Xunit.Abstractions;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;
namespace TekstilCAD.Tests;

public class OffsetPocketDimensionDiagnosticTests(ITestOutputHelper output)
{
    private static readonly PocketRatioRange One=new(1,1,1,1);
    private static readonly PocketRatioRange Half=new(1,2,1,2);
    private static GridPoint[] Pocket(long shift=0)=>[new(shift+52,shift+100),new(shift+52,shift+20),new(shift+48,shift+20),new(shift+48,shift+100)];
    private static GridPoint[] OffsetPocket(long shift=0)=>[new(shift+51,shift+101),new(shift+51,shift+21),new(shift+49,shift+21),new(shift+49,shift+101)];
    private static bool Verify(GridPoint[] a,GridPoint[] b,out string reason)=>OffsetPocketDimensionDiagnostic.TryVerify(a,b,0,Half,Half,One,One,out _,out reason);
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void KnownMiteredPocketHasHalfWidthAndUnchangedDepthInBothDirections(bool reverse)
    {
        var source=Pocket();var candidate=OffsetPocket();
        if(reverse) { Array.Reverse(source);Array.Reverse(candidate); }
        Assert.True(OffsetPocketDimensionDiagnostic.TryVerify(source,candidate,0,Half,Half,One,One,out var evidence,out var reason),reason);
        Assert.Equal(new BigInteger(16),evidence!.Source.MouthSquared);Assert.Equal(new BigInteger(4),evidence.Candidate.MouthSquared);
        Assert.Contains("production suitability remain unverified",reason);
    }
    [Fact]
    public void WidthLossDepthLossAndSwappedFloorAreRejected()
    {
        var source=Pocket();var candidate=OffsetPocket();candidate[2]=candidate[1];
        Assert.False(Verify(source,candidate,out _));
        candidate=OffsetPocket();candidate[1]=new(51,61);candidate[2]=new(49,61);
        Assert.False(Verify(source,candidate,out _));
        candidate=OffsetPocket();(candidate[1],candidate[2])=(candidate[2],candidate[1]);
        Assert.False(Verify(source,candidate,out _));
    }
    [Fact]
    public void ExactRatiosRejectEvenOneGridUnitOutsideSpecifiedBoundary()
    {
        var source=Pocket();var candidate=OffsetPocket();Assert.True(Verify(source,candidate,out var reason),reason);
        candidate[3]=new(48,101);candidate[2]=new(48,21);
        Assert.False(Verify(source,candidate,out _));
    }
    [Fact]
    public void CallerSelectedSinglePocketCannotCertifyOtherFeatures()
    {
        var source=Pocket().Concat(Pocket(1000)).ToArray();var candidate=source.ToArray();
        candidate[6]=candidate[5];
        Assert.True(OffsetPocketDimensionDiagnostic.TryVerify(source,candidate,0,One,One,One,One,out _,out var reason),reason);
        Assert.Contains("unselected features",reason);
    }
    [Fact]
    public void LargeOriginAndCancellationAreHandledWithIntegerArithmetic()
    {
        const long shift=9_000_000_000_000_000_000;
        Assert.True(Verify(Pocket(shift),OffsetPocket(shift),out var reason),reason);
        using var c=new CancellationTokenSource();c.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>OffsetPocketDimensionDiagnostic.TryVerify(Pocket(),OffsetPocket(),0,Half,Half,One,One,out _,out _,c.Token));
    }
    [Fact]
    public void RejectsInvalidOrZeroRatioRanges()
    {
        var invalid=new PocketRatioRange(0,1,1,1);
        Assert.False(OffsetPocketDimensionDiagnostic.TryVerify(Pocket(),OffsetPocket(),0,invalid,Half,One,One,out _,out _));
        var reversed=new PocketRatioRange(2,1,1,1);
        Assert.False(OffsetPocketDimensionDiagnostic.TryVerify(Pocket(),OffsetPocket(),0,reversed,Half,One,One,out _,out _));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LobedCandidateReportsOnlyExplicitFourVertexWindows(bool reverse)
    {
        const long grid=1_000_000;
        var source=Enumerable.Range(0,512).Select(i=>
        {
            var angle=Math.Tau*i/512;var radius=1000+200*Math.Cos(3*angle);
            return new GridPoint((long)Math.Round(radius*Math.Cos(angle)*grid,MidpointRounding.AwayFromZero),
                (long)Math.Round(radius*Math.Sin(angle)*grid,MidpointRounding.AwayFromZero));
        }).ToArray();
        if(reverse)Array.Reverse(source);
        var method=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof",true)!.GetMethod("TryCreate",BindingFlags.NonPublic|BindingFlags.Static)!;
        object?[] args=[source,grid,null,null,CancellationToken.None];Assert.True((bool)method.Invoke(null,args)!, (string?)args[3]);
        var candidate=Assert.IsAssignableFrom<IReadOnlyList<GridPoint>>(args[2]);var broad=new PocketRatioRange(1,10,10,1);var verified=0;
        for(int i=0;i<source.Length;i++)
            if(OffsetPocketDimensionDiagnostic.TryVerify(source,candidate,i,broad,broad,broad,broad,out _,out _))verified++;
        output.WriteLine($"reverse={reverse}; explicitly selected four-vertex windows meeting broad bounds={verified}/512; not a feature inventory or production tolerance");
        Assert.InRange(verified,0,512);
    }
}
