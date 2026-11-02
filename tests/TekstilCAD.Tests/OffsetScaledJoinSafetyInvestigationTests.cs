using System.Reflection;
using Clipper2Lib;
using TekstilCAD.Patterns;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

/// <summary>Numerical experiment only; does not call or authorize production fallback.</summary>
public sealed class OffsetScaledJoinSafetyInvestigationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScaledConstructionDoesNotMakeLocalRoundTripAOneGridFeatureProof(bool reverse)
    {
        const long u=1_000_000;
        Path64 source=[new(0,0),new(100*u,0),new(100*u,100*u),new(50*u+2,100*u),
            new(50*u+2,100*u-1),new(50*u,100*u-1),new(50*u,100*u),new(0,100*u)];
        if(reverse)source.Reverse();
        var forward=HighPrecisionForward(source,u);
        var recovered=Assert.Single(Clipper.InflatePaths(new Paths64 {forward},-u,JoinType.Miter,EndType.Polygon,2.0));
        bool local=Matches(source,recovered) && Matches(recovered,source);
        output.WriteLine($"reverse={reverse}; feature_depth_grid=1; forward_vertices={forward.Count}; recovered_vertices={recovered.Count}; local8={local}");
        Assert.Equal(4,forward.Count);
        Assert.Equal(4,recovered.Count);
        // The complete production path has a separate sharp-short-edge veto.
        // This test intentionally isolates the later local check alone.
        Assert.True(local, "This counterexample demonstrates the local check alone cannot certify a one-grid feature.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScaledConstructionDoesNotCertifyClosedFourMillimeterRecess(bool reverse)
    {
        const long u=1_000_000;
        Path64 source=[new(0,0),new(100*u,0),new(100*u,100*u),new(52*u,100*u),
            new(52*u,20*u),new(48*u,20*u),new(48*u,100*u),new(0,100*u)];
        if(reverse)source.Reverse();
        var forward=HighPrecisionForward(source,5*u);
        var recovered=Assert.Single(Clipper.InflatePaths(new Paths64 {forward},-5*u,JoinType.Miter,EndType.Polygon,2.0));
        Assert.False(Matches(source,recovered) && Matches(recovered,source));
    }

    private static Path64 HighPrecisionForward(Path64 source,long delta)
    {
        var high=new Path64(source.Select(p=>new Point64(checked(p.X*10),checked(p.Y*10))));
        var result=Assert.Single(Clipper.InflatePaths(new Paths64 {high},checked(delta*10),JoinType.Miter,EndType.Polygon,2.0));
        return new Path64(result.Select(p=>new Point64((long)Math.Round(p.X/10d,MidpointRounding.AwayFromZero),
            (long)Math.Round(p.Y/10d,MidpointRounding.AwayFromZero))));
    }

    private static bool Matches(Path64 a,Path64 b)
    {
        var method=typeof(PatternTools).GetMethod("LocallyMatches",BindingFlags.Static|BindingFlags.NonPublic)!;
        return (bool)method.Invoke(null,[a,b,8d])!;
    }
}
