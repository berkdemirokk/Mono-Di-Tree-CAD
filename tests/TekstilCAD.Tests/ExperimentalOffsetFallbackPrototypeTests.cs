using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

public sealed class ExperimentalOffsetFallbackPrototypeTests(ITestOutputHelper log)
{
    private static PatternPiece Lobed()
    {
        var m=typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.NonPublic|BindingFlags.Static)!;
        return (PatternPiece)m.Invoke(null,[512,true])!;
    }
    private static double MaximumTurnDegrees(IReadOnlyList<Point2> points)
    {
        double max=0;
        for(int i=0;i<points.Count;i++)
        {
            var a=points[(i+points.Count-1)%points.Count];var b=points[i];var c=points[(i+1)%points.Count];
            var u=b-a;var v=c-b;max=Math.Max(max,Math.Abs(Math.Atan2(u.X*v.Y-u.Y*v.X,u.X*v.X+u.Y*v.Y))*180/Math.PI);
        }
        return max;
    }
    private static int TurnSignTransitions(IReadOnlyList<Point2> points)
    {
        int[] signs=new int[points.Count];
        for(int i=0;i<points.Count;i++)
        {
            var u=points[i]-points[(i+points.Count-1)%points.Count];var v=points[(i+1)%points.Count]-points[i];
            signs[i]=Math.Sign(u.X*v.Y-u.Y*v.X);
        }
        return Enumerable.Range(0,points.Count).Count(i=>signs[i]!=signs[(i+points.Count-1)%points.Count]);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealLobedOffsetReachesExperimentalCandidateButFeatureAmbiguityRetainsFailure(bool reverse)
    {
        var source=Lobed();if(reverse)source.Points.Reverse();var before=source.Points.ToArray();
        var original=Assert.Throws<OffsetLocalDetailUnverifiedException>(()=>PatternTools.Offset(source,1));
        var result=ExperimentalOffsetFallbackPrototype.TryOffset(source,1);
        Assert.Null(result.Piece);Assert.NotNull(result.OriginalFailure);Assert.Equal(original.Message,result.OriginalFailure.Message);
        Assert.True(result.NumericalChecksPassed,result.Reason);Assert.Contains("Feature",result.Reason);
        Assert.Equal(before,source.Points);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmallAngleDoesNotProveAbsenceOfPhysicalNotch(bool reverse)
    {
        var smooth=Lobed();var physical=PatternTools.InsertNotch(smooth,146,.5,6,.05);
        if(reverse){smooth.Points.Reverse();physical.Points.Reverse();}
        Assert.Null(physical.Notches);Assert.Equal(515,physical.Points.Count);
        var smoothAngle=MaximumTurnDegrees(smooth.Points);var notchAngle=MaximumTurnDegrees(physical.Points);
        Assert.InRange(smoothAngle,0,2);Assert.InRange(notchAngle,0,2);
        var result=ExperimentalOffsetFallbackPrototype.TryOffset(physical,1);
        log.WriteLine($"reverse={reverse}; smooth max={smoothAngle:F6}°, V max={notchAngle:F6}°; numerical={result.NumericalChecksPassed}; reason={result.Reason}");
        Assert.Null(result.Piece);Assert.NotNull(result.OriginalFailure);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DifferentLegacyGridAndExistingVetoesAreNotBypassed(bool reverse)
    {
        var large=Lobed();large.Points=large.Points.Select(p=>new Point2(p.X+50_000_000,p.Y+50_000_000)).ToList();
        var notch=new PatternPiece{Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(50.000002,100),
            new(50.000002,99.999999),new(50,99.999999),new(50,100),new(0,100)]};
        var channel=new PatternPiece{Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(52,100),new(52,20),new(48,20),new(48,100),new(0,100)]};
        if(reverse){large.Points.Reverse();notch.Points.Reverse();channel.Points.Reverse();}
        var shifted=ExperimentalOffsetFallbackPrototype.TryOffset(large,1);
        Assert.Null(shifted.Piece);Assert.NotNull(shifted.OriginalFailure);Assert.Contains("differs",shifted.Reason);
        Assert.ThrowsAny<ArgumentException>(()=>ExperimentalOffsetFallbackPrototype.TryOffset(notch,1));
        var closed=ExperimentalOffsetFallbackPrototype.TryOffset(channel,5);
        Assert.Null(closed.Piece);Assert.NotNull(closed.OriginalFailure);Assert.False(closed.NumericalChecksPassed);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameVertexCountSmallTurnsAndInflectionCountDoNotProveNoLocalIndentation(bool reverse)
    {
        var baseline=Lobed();var changed=baseline.Clone();const int index=74;
        var a=changed.Points[index-1];var b=changed.Points[index];var c=changed.Points[index+1];
        var chord=c-a;double length=Geometry.Distance(a,c);
        changed.Points[index]=b+new Point2(-chord.Y/length,chord.X/length)*.05;
        double SignedDepth(Point2 x)=>(chord.X*(x.Y-a.Y)-chord.Y*(x.X-a.X))/length;
        Assert.True(SignedDepth(b)>0);
        Assert.InRange(SignedDepth(changed.Points[index])-SignedDepth(b),.049999,.050001);
        if(reverse){baseline.Points.Reverse();changed.Points.Reverse();}
        Assert.Equal(512,baseline.Points.Count);Assert.Equal(512,changed.Points.Count);Assert.Null(changed.Notches);
        Assert.InRange(MaximumTurnDegrees(baseline.Points),0,2);Assert.InRange(MaximumTurnDegrees(changed.Points),0,2);
        Assert.Equal(6,TurnSignTransitions(baseline.Points));Assert.Equal(6,TurnSignTransitions(changed.Points));
        var result=ExperimentalOffsetFallbackPrototype.TryOffset(changed,1);
        log.WriteLine($"reverse={reverse}; source and modified both 512 points, six turn transitions and max turn <2°; numerical={result.NumericalChecksPassed}; {result.Reason}");
        Assert.Null(result.Piece);Assert.NotNull(result.OriginalFailure);
    }
    [Fact]
    public void ExistingSuccessAndOtherErrorsRemainOriginal()
    {
        var square=new PatternPiece{Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(0,100)]};
        var result=ExperimentalOffsetFallbackPrototype.TryOffset(square,1);
        Assert.NotNull(result.Piece);Assert.Null(result.OriginalFailure);
        square.Notches=[new NotchMark{VertexIndex=0,Position=square.Points[0]}];
        Assert.ThrowsAny<ArgumentException>(()=>ExperimentalOffsetFallbackPrototype.TryOffset(square,1));
        using var c=new CancellationTokenSource();c.Cancel();
        Assert.Throws<OperationCanceledException>(()=>ExperimentalOffsetFallbackPrototype.TryOffset(Lobed(),1,c.Token));
    }
}
