using System.Reflection;
using System.Runtime.ExceptionServices;
using Clipper2Lib;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

/// <summary>
/// Independent hand-derived grid examples for an experimental comparison.
/// A passing result is not proof of manufacturing fitness or feature policy.
/// </summary>
public sealed class OffsetForwardBoundaryProofTests
{
    private readonly ITestOutputHelper output;
    public OffsetForwardBoundaryProofTests(ITestOutputHelper output) => this.output=output;
    private const long Grid = 1_000_000;
    private static GridPoint P(long x, long y) => new(x * Grid, y * Grid);
    private static readonly GridPoint[] LSource = [P(0,0),P(8,0),P(8,4),P(4,4),P(4,8),P(0,8)];
    private static readonly GridPoint[] LExpected = [P(-1,-1),P(9,-1),P(9,5),P(5,5),P(5,9),P(-1,9)];
    private static readonly GridPoint[] USource = [P(0,0),P(100,0),P(100,100),P(60,100),P(60,20),P(40,20),P(40,100),P(0,100)];
    private static readonly GridPoint[] UExpected = [P(-2,-2),P(102,-2),P(102,102),P(58,102),P(58,22),P(42,22),P(42,102),P(-2,102)];

    private static (bool Accepted, string Reason) Verify(IReadOnlyList<GridPoint> source, long delta,
        IReadOnlyList<GridPoint> forward, CancellationToken token = default)
    {
        var type = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetForwardBoundaryProof", true)!;
        var method = type.GetMethod("TryVerify", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] args = [source, delta, forward, null, token];
        try { return ((bool)method.Invoke(null,args)!, (string)args[3]!); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static GridPoint[] Arrange(GridPoint[] points, bool reverse, int shift)
    {
        var value=points.ToArray();
        if(reverse)Array.Reverse(value);
        shift%=value.Length;
        return value.Skip(shift).Concat(value.Take(shift)).ToArray();
    }

    [Theory]
    [InlineData(false,0,0)]
    [InlineData(false,3,2)]
    [InlineData(true,0,4)]
    [InlineData(true,2,1)]
    public void HandDerivedLMatchesForEitherOrientationAndCyclicStart(bool reverse,int sourceStart,int outputStart)
    {
        var source=Arrange(LSource,reverse,sourceStart);var output=Arrange(LExpected,reverse,outputStart);
        var beforeSource=source.ToArray();var beforeOutput=output.ToArray();
        var result=Verify(source,Grid,output);
        Assert.True(result.Accepted,result.Reason);
        Assert.Equal(beforeSource,source);Assert.Equal(beforeOutput,output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HandDerivedWideUAndCollinearOutputSubdivisionAreAccepted(bool reverse)
    {
        var source=Arrange(USource,reverse,2);var output=Arrange(UExpected,reverse,3).ToList();
        var a=output[0];var b=output[1];
        output.Insert(1,new GridPoint((a.X+b.X)/2,(a.Y+b.Y)/2));
        var beforeSource=source.ToArray();var beforeOutput=output.ToArray();
        var result=Verify(source,2*Grid,output);
        Assert.True(result.Accepted,result.Reason);
        Assert.Equal(beforeSource,source);Assert.Equal(beforeOutput,output);
    }

    [Fact]
    public void RecessRemovedOrExtraProtrusionIsNeverAccepted()
    {
        GridPoint[] closedRecess=[P(-2,-2),P(102,-2),P(102,102),P(-2,102)];
        GridPoint[] protrusion=[P(-2,-2),P(102,-2),P(102,102),P(80,102),P(80,103),P(79,103),P(79,102),P(58,102),P(58,22),P(42,22),P(42,102),P(-2,102)];
        foreach(var output in new[] {closedRecess,protrusion})
        {
            var before=output.ToArray();var result=Verify(USource,2*Grid,output);
            Assert.False(result.Accepted);Assert.False(string.IsNullOrWhiteSpace(result.Reason));Assert.Equal(before,output);
        }
    }

    [Fact]
    public void ExactBoundaryPolicyRejectsEvenOneGridUnitAndReversedOnlyOutput()
    {
        var altered=LExpected.ToArray();altered[3]=new(altered[3].X+1,altered[3].Y);
        Assert.False(Verify(LSource,Grid,altered).Accepted);
        Assert.False(Verify(LSource,Grid,Arrange(LExpected,true,0)).Accepted);
    }

    [Fact]
    public void OneGridNotchOrBacktrackingSubdivisionCannotMasqueradeAsTheSameBoundary()
    {
        // Both ends of each altered portion still lie on the reference edge;
        // a vertex-only or unordered point-set comparison must not accept it.
        var tinyNotch=LExpected.ToList();
        tinyNotch.InsertRange(1,[P(2,-1),new GridPoint(3*Grid,-Grid+1),P(4,-1)]);
        var reverseTrace=LExpected.ToList();
        reverseTrace.InsertRange(1,[P(4,-1),P(2,-1)]);
        Assert.False(Verify(LSource,Grid,tinyNotch).Accepted);
        Assert.False(Verify(LSource,Grid,reverseTrace).Accepted);
    }

    [Fact]
    public void MissingCornerAndLongRangeOverflowDoNotBecomeAccepted()
    {
        var shortcut=LExpected.Where((_,i)=>i!=3).ToArray();
        Assert.False(Verify(LSource,Grid,shortcut).Accepted);
        GridPoint[] extreme=[new(long.MinValue,0),new(long.MaxValue,0),new(long.MaxValue,100),new(long.MinValue,100)];
        Assert.False(Verify(extreme,1,extreme).Accepted);
    }

    [Fact]
    public void OneExplicitClosingPointIsAcceptedButRepeatedInteriorPointsAreRejected()
    {
        var closed=LExpected.Concat([LExpected[0]]).ToArray();
        var accepted=Verify(LSource,Grid,closed);
        Assert.True(accepted.Accepted,accepted.Reason);
        var duplicate=LExpected.ToList();duplicate.Insert(2,duplicate[1]);
        Assert.False(Verify(LSource,Grid,duplicate).Accepted);
    }

    [Fact]
    public void CancellationIsPropagatedWithoutMutatingInputs()
    {
        var source=LSource.ToArray();var output=LExpected.ToArray();
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>Verify(source,Grid,output,cancellation.Token));
        Assert.Equal(LSource,source);Assert.Equal(LExpected,output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LobedContourReportsExperimentalComparisonAgainstActualForwardResult(bool reverse)
    {
        // This reproduces the geometry and quantization of the existing 512-point
        // regression; Clipper is the observed output, never the independent oracle.
        var source=new List<GridPoint>();
        for(var i=0;i<512;i++)
        {
            var angle=2*Math.PI*i/512;
            var radius=1000+200*Math.Cos(3*angle);
            source.Add(new GridPoint(
                checked((long)Math.Round(radius*Math.Cos(angle)*Grid,MidpointRounding.AwayFromZero)),
                checked((long)Math.Round(radius*Math.Sin(angle)*Grid,MidpointRounding.AwayFromZero))));
        }
        if(reverse)source.Reverse();
        var before=source.ToArray();
        var paths=Clipper.InflatePaths(new Paths64 { new Path64(source.Select(p=>new Point64(p.X,p.Y))) },
            Grid,JoinType.Miter,EndType.Polygon,2.0);
        var forward=Assert.Single(paths).Select(p=>new GridPoint(p.X,p.Y)).ToArray();
        var result=Verify(source,Grid,forward);
        output.WriteLine($"reverse={reverse}; source_points={source.Count}; forward_points={forward.Length}; accepted={result.Accepted}; reason={result.Reason}");
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        Assert.Equal(before,source);
        // Both outcomes are observations only. Acceptance here neither approves
        // production use nor proves local feature preservation.
    }
}
