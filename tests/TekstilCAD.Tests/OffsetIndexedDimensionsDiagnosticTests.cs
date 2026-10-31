using System.Reflection;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetIndexedDimensionsDiagnosticTests
{
    private static (bool Ok, string Reason) Verify(IReadOnlyList<GridPoint> s, IReadOnlyList<GridPoint> t, int p, int q)
    {
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetIndexedDimensionsDiagnostic", true)!
            .GetMethod("TryVerify", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [s, t, p, q, null, CancellationToken.None];
        var ok = (bool)method.Invoke(null, args)!;
        return (ok, (string)args[4]!);
    }

    [Fact]
    public void ExactHalfScaleIsAcceptedAtBoundaryAndRejectedAboveIt()
    {
        GridPoint[] source = [new(0,0), new(100,0), new(100,100), new(0,100)];
        GridPoint[] target = [new(0,0), new(50,0), new(50,50), new(0,50)];
        Assert.True(Verify(source,target,1,2).Ok);
        Assert.False(Verify(source,target,51,100).Ok);
    }

    [Fact]
    public void NarrowRecessClosureIsRejectedEvenWhenBoundingBoxIsUnchanged()
    {
        GridPoint[] source = [new(0,0), new(1000,0), new(1000,1000), new(510,1000), new(510,100), new(490,100), new(490,1000), new(0,1000)];
        GridPoint[] closed = [new(0,0), new(1000,0), new(1000,1000), new(500,1000), new(500,100), new(500,100), new(500,1000), new(0,1000)];
        Assert.False(Verify(source,closed,1,2).Ok);
    }

    [Fact]
    public void EdgeInteriorApproachIsDetectedEvenWhenEndpointsMoveLittle()
    {
        GridPoint[] source = [new(0,0), new(1000,0), new(1000,1000), new(510,1000), new(510,100), new(490,100), new(490,1000), new(0,1000)];
        GridPoint[] target = [new(0,0), new(1000,0), new(1000,1000), new(501,1000), new(501,100), new(499,100), new(499,1000), new(0,1000)];
        var result = Verify(source,target,1,2);
        Assert.False(result.Ok);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LobedIndexedCandidateReportsDimensionEvidence(bool reverse)
    {
        const long grid = 1_000_000;
        var source=Enumerable.Range(0,512).Select(i => {
            double a=Math.Tau*i/512, r=1000+200*Math.Cos(3*a);
            return new GridPoint((long)Math.Round(r*Math.Cos(a)*grid,MidpointRounding.AwayFromZero),
                (long)Math.Round(r*Math.Sin(a)*grid,MidpointRounding.AwayFromZero));
        }).ToArray();
        if(reverse) Array.Reverse(source);
        var method=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof",true)!
            .GetMethod("TryCreate",BindingFlags.NonPublic|BindingFlags.Static)!;
        object?[] args=[source,grid,null,null,CancellationToken.None];
        Assert.True((bool)method.Invoke(null,args)!, (string?)args[3]);
        var target=Assert.IsAssignableFrom<IReadOnlyList<GridPoint>>(args[2]);
        var result=Verify(source,target,1,2);
        Assert.True(result.Ok,result.Reason);
    }
}
