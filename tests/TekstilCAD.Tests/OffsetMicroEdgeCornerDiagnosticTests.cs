using System.Numerics;
using Clipper2Lib;
using TekstilCAD.Patterns;
using Xunit.Abstractions;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public class OffsetMicroEdgeCornerDiagnosticTests(ITestOutputHelper output)
{
    private static bool Check(GridPoint[] source,int corner,GridPoint[] forward,long maxLength=2,long maxDistance=3,long exclusion=10,long numerator=1,long denominator=1000)
        => OffsetMicroEdgeCornerDiagnostic.TryVerify(source,corner,forward,0,10,maxLength,maxDistance,exclusion,numerator,denominator,out _,out _);
    [Theory]
    [InlineData(0L)] [InlineData(50_000_000_000_000L)]
    public void NearStraightCornerAndShortEdgeCanSatisfyExplicitBoundsAtEitherOrigin(long shift)
    {
        GridPoint P(long x,long y)=>new(x+shift,y+shift);
        GridPoint[] source=[P(0,0),P(1000,0),P(2000,1),P(2000,1000),P(0,1000)];
        GridPoint[] forward=[P(1000,-10),P(1001,-10),P(2000,1000)];
        var before=source.ToArray();
        Assert.True(OffsetMicroEdgeCornerDiagnostic.TryVerify(source,1,forward,0,10,2,3,10,1,1000,out var evidence,out var reason),reason);
        Assert.NotNull(evidence);Assert.Equal(BigInteger.One,evidence.EdgeLengthSquared);Assert.Contains("unverified",reason);Assert.Equal(before,source);
    }
    [Fact]
    public void SharpNotchAndNearbyNonadjacentVertexAreRejected()
    {
        GridPoint[] sharp=[new(0,0),new(1000,0),new(1000,10),new(2000,10),new(2000,1000),new(0,1000)];
        GridPoint[] forward=[new(1000,-10),new(1001,-10),new(2000,1000)];
        Assert.False(Check(sharp,1,forward));
        GridPoint[] crowded=[new(0,0),new(1000,0),new(2000,1),new(2000,1000),new(1000,1),new(0,1000)];
        Assert.False(Check(crowded,1,forward));
    }
    [Fact]
    public void ShallowConcaveCornerCanPassLocalBoundsSoThisIsNotANotchClassifier()
    {
        GridPoint[] shallow=[new(0,0),new(1000,0),new(2000,-1),new(2000,1000),new(0,1000)];
        GridPoint[] forward=[new(1000,-10),new(1001,-10),new(2000,1000)];
        Assert.True(Check(shallow,1,forward));
        // A negative turn in a CCW contour may be part of a real shallow feature.
        // Local bounds alone cannot justify deleting the short forward edge.
    }
    [Fact]
    public void TooLongEdgeTooFarMiterInvalidArgumentsAndCancellationReject()
    {
        GridPoint[] source=[new(0,0),new(1000,0),new(2000,1),new(2000,1000),new(0,1000)];
        GridPoint[] forward=[new(1000,-10),new(1003,-10),new(2000,1000)];
        Assert.False(Check(source,1,forward));forward[1]=new(1100,-10);Assert.False(Check(source,1,forward,maxLength:200));
        Assert.False(OffsetMicroEdgeCornerDiagnostic.TryVerify(source,1,forward,0,10,0,3,10,1,1000,out _,out _));
        using var c=new CancellationTokenSource();c.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>OffsetMicroEdgeCornerDiagnostic.TryVerify(source,1,forward,0,10,2,3,10,1,1000,out _,out _,c.Token));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualLobedMicroEdgeHasLocalIntervalEvidenceWithoutApprovingOffset(bool reverse)
    {
        const long grid=1_000_000;
        var source=Enumerable.Range(0,512).Select(i=> { var a=Math.Tau*i/512;var r=1000+200*Math.Cos(3*a);return new GridPoint((long)Math.Round(r*Math.Cos(a)*grid,MidpointRounding.AwayFromZero),(long)Math.Round(r*Math.Sin(a)*grid,MidpointRounding.AwayFromZero)); }).ToArray();
        if(reverse)Array.Reverse(source);
        var path=new Path64(source.Select(p=>new Point64(p.X,p.Y)));
        var actual=Assert.Single(Clipper.InflatePaths(new Paths64 { path },grid,JoinType.Miter,EndType.Polygon,2));
        var forward=actual.Select(p=>new GridPoint(p.X,p.Y)).ToArray();
        var matched=new List<(int Source,int Edge)>();
        for(int e=0;e<forward.Length;e++)
        {
            var dx=(BigInteger)forward[(e+1)%forward.Length].X-forward[e].X;
            var dy=(BigInteger)forward[(e+1)%forward.Length].Y-forward[e].Y;
            if(dx*dx+dy*dy>4 || dx*dx+dy*dy==0)continue;
            for(int i=0;i<source.Length;i++)
                if(OffsetMicroEdgeCornerDiagnostic.TryVerify(source,i,forward,e,grid,2,500,10000,1,1000,out _,out _))matched.Add((i,e));
        }
        Assert.NotEmpty(matched);
        foreach(var pair in matched)output.WriteLine($"reverse={reverse} source-corner={pair.Source} forward-micro-edge={pair.Edge}");
        // This is only proximity and local angle evidence; production still rejects
        // the known inverse mismatch and no notch-preservation claim is made.
    }
}
