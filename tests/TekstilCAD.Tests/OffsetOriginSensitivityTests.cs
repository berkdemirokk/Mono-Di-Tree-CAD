using System.Reflection;
using System.Runtime.ExceptionServices;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using Xunit.Abstractions;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public class OffsetOriginSensitivityTests(ITestOutputHelper output)
{
    private static (int GlobalForward,int LocalForward,int GlobalReverse,int LocalReverse,bool ForwardEqual,bool ReverseEqual) Compare(
        IReadOnlyList<GridPoint> source,long delta,GridPoint origin,CancellationToken token=default)
    {
        var type=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetOriginSensitivityDiagnostic",true)!;
        var method=type.GetMethod("Compare",BindingFlags.Static|BindingFlags.NonPublic)!;
        try
        {
            var value=method.Invoke(null,[source,delta,origin,token])!;var t=value.GetType();
            int I(string name)=>(int)t.GetProperty(name)!.GetValue(value)!;
            bool B(string name)=>(bool)t.GetProperty(name)!.GetValue(value)!;
            return (I("GlobalForwardPaths"),I("LocalForwardPaths"),I("GlobalReversePaths"),I("LocalReversePaths"),B("ForwardExactlyEqual"),B("ReverseExactlyEqual"));
        }
        catch(TargetInvocationException ex) when(ex.InnerException is not null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw();throw; }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AxisAlignedRectangleMatchesWithFarOrigin(bool reverse)
    {
        const long o=50_000_000_000_000;
        GridPoint[] path=[new(o,o),new(o+100_000_000,o),new(o+100_000_000,o+50_000_000),new(o,o+50_000_000)];
        if(reverse)Array.Reverse(path);var before=path.ToArray();var r=Compare(path,1_000_000,new(o,o));
        Assert.Equal((1,1,1,1),(r.GlobalForward,r.LocalForward,r.GlobalReverse,r.LocalReverse));
        Assert.True(r.ForwardEqual);Assert.True(r.ReverseEqual);Assert.Equal(before,path);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LobedForwardAndReverseCanBeComparedAtFarOriginWithoutAssumingImprovement(bool reverse)
    {
        const long grid=1_000_000, origin=50_000_000_000_000;
        var path=Enumerable.Range(0,512).Select(i=>
        {
            var a=Math.Tau*i/512;var radius=1000+200*Math.Cos(3*a);
            return new GridPoint(origin+(long)Math.Round(radius*Math.Cos(a)*grid,MidpointRounding.AwayFromZero),
                origin+(long)Math.Round(radius*Math.Sin(a)*grid,MidpointRounding.AwayFromZero));
        }).ToArray();
        if(reverse)Array.Reverse(path);var copy=path.ToArray();
        var observation=Compare(path,grid,new(origin,origin));
        output.WriteLine($"reverse={reverse}, global/local forward paths={observation.GlobalForward}/{observation.LocalForward}, inverse={observation.GlobalReverse}/{observation.LocalReverse}, exact forward={observation.ForwardEqual}, exact inverse={observation.ReverseEqual}");
        Assert.Equal(copy,path);
        // Observational only: either answer is useful, neither proves feature preservation.
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EqualOriginsCannotAuthorizeKnownCollapsedRecess(bool reverse)
    {
        const long scale=1_000_000;
        GridPoint[] contour=[new(0,0),new(100*scale,0),new(100*scale,100*scale),new(52*scale,100*scale),
            new(52*scale,99_980_000),new(48*scale,99_980_000),new(48*scale,100*scale),new(0,100*scale)];
        if(reverse)Array.Reverse(contour);
        var r=Compare(contour,5*scale,new(0,0));
        Assert.True(r.ForwardEqual);Assert.True(r.ReverseEqual);
        var piece=new PatternPiece { Points=contour.Select(p=>new Point2((double)p.X/scale,(double)p.Y/scale)).ToList() };
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.Offset(piece,5));
    }
    [Fact]
    public void RejectsLimitsAndObservesCancellation()
    {
        GridPoint[] path=[new(0,0),new(10,0),new(0,10)];
        Assert.Throws<ArgumentException>(()=>Compare(path,0,new(0,0)));
        Assert.Throws<ArgumentException>(()=>Compare(path,1,new(long.MaxValue,0)));
        using var c=new CancellationTokenSource();c.Cancel();Assert.ThrowsAny<OperationCanceledException>(()=>Compare(path,1,new(0,0),c.Token));
    }
}
