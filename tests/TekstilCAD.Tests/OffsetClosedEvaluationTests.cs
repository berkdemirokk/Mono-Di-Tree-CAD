using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetClosedEvaluationTests
{
    private static object Evaluate(PatternPiece piece,double mm,double point=.000001,double length=.000003,double height=.000003)
    {
        var method=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetClosedEvaluation",true)!
            .GetMethod("Evaluate",BindingFlags.NonPublic|BindingFlags.Static)!;
        return method.Invoke(null,[piece,mm,point,length,height,CancellationToken.None])!;
    }
    private static T Value<T>(object result,string name)=>(T)result.GetType().GetProperty(name)!.GetValue(result)!;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualLobedSourceCanOnlyYieldExplicitlyUnapprovedEvaluation(bool reverse)
    {
        var fixture=typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.Static|BindingFlags.NonPublic)!;
        var source=(PatternPiece)fixture.Invoke(null,[512,true])!;
        if(reverse)source.Points.Reverse();var before=source.Points.ToArray();var id=source.Id;
        var result=Evaluate(source,1);
        Assert.True(Value<bool>(result,"EvaluationPassed"),Value<string>(result,"Reason"));
        var candidate=Value<IReadOnlyList<Point2>>(result,"DiagnosticCandidate");Assert.Equal(512,candidate.Count);
        Assert.True(Value<bool>(result,"OriginalPreLocalChecksPassed"));
        Assert.False(Value<bool>(result,"OriginalPostLocalChecksPassed"));
        Assert.True(Value<bool>(result,"SourceGridBudgetPassed"));
        Assert.True(Value<bool>(result,"IndexedSweepPassed"));
        Assert.True(Value<bool>(result,"CandidateInverseGuardsPassed"));
        Assert.False(Value<bool>(result,"FeaturesCertified"));
        Assert.False(Value<bool>(result,"ProductionApproved"));
        Assert.Equal(.000001,Value<double>(result,"SourcePointBudgetMm"));
        Assert.Equal(.000003,Value<double>(result,"SourceLengthBudgetMm"));
        Assert.Equal(.000003,Value<double>(result,"SourceHeightBudgetMm"));
        Assert.Equal(1,Value<int>(result,"CandidateEdgeBandGrid"));
        Assert.Equal(8,Value<int>(result,"InverseSampleToleranceGrid"));
        var immutable=Assert.IsAssignableFrom<IList<Point2>>(candidate);
        Assert.Throws<NotSupportedException>(()=>immutable[0]=new Point2(0,0));
        var bounds=Geometry.Bounds(candidate);var sourceBounds=Geometry.Bounds(before);
        Assert.InRange(bounds.MaxX-sourceBounds.MaxX,.999,1.01);
        Assert.False(Geometry.HasSelfIntersections(candidate));
        // Independent shifted-line intersection for each mapped source corner.
        var grid=before.Select(p=>new Point2(Math.Round(p.X*1_000_000,MidpointRounding.AwayFromZero)/1_000_000,
            Math.Round(p.Y*1_000_000,MidpointRounding.AwayFromZero)/1_000_000)).ToArray();
        int orientation=Math.Sign(Geometry.SignedArea(grid));
        static double Cross(Point2 a,Point2 b)=>a.X*b.Y-a.Y*b.X;
        for(int i=0;i<grid.Length;i++)
        {
            var a=grid[(i+grid.Length-1)%grid.Length];var b=grid[i];var c=grid[(i+1)%grid.Length];
            var u=b-a;var v=c-b;double lu=Geometry.Distance(a,b),lv=Geometry.Distance(b,c);
            var q=b+new Point2(orientation*u.Y/lu,-orientation*u.X/lu);
            var r=b+new Point2(orientation*v.Y/lv,-orientation*v.X/lv);
            var expected=q+u*(Cross(r-q,v)/Cross(u,v));
            Assert.InRange(Geometry.Distance(expected,candidate[i]),0,.000002);
        }
        Assert.Throws<OffsetLocalDetailUnverifiedException>(()=>PatternTools.Offset(source,1));
        Assert.Equal(before,source.Points);Assert.Equal(id,source.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneGridNotchNeverEscapesOriginalVeto(bool reverse)
    {
        var piece=new PatternPiece {Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(50.000002,100),
            new(50.000002,99.999999),new(50,99.999999),new(50,100),new(0,100)]};
        if(reverse)piece.Points.Reverse();var before=piece.Points.ToArray();
        var result=Evaluate(piece,1);
        Assert.False(Value<bool>(result,"EvaluationPassed"));Assert.Empty(Value<IReadOnlyList<Point2>>(result,"DiagnosticCandidate"));
        Assert.Equal(before,piece.Points);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedChannelAndSourceTurnCollapseRemainRejected(bool reverse)
    {
        var channel=new PatternPiece {Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(52,100),
            new(52,20),new(48,20),new(48,100),new(0,100)]};
        var shallow=new PatternPiece {Closed=true,Points=[new(0,0),new(100,.0000004),new(200,0),new(200,100),new(0,100)]};
        if(reverse){channel.Points.Reverse();shallow.Points.Reverse();}
        Assert.False(Value<bool>(Evaluate(channel,5),"EvaluationPassed"));
        Assert.False(Value<bool>(Evaluate(shallow,1),"EvaluationPassed"));
    }

    [Fact]
    public void OverflowMissingBudgetAndOriginalSuccessCannotBecomeFallback()
    {
        var edge=new PatternPiece {Closed=true,Points=[new(99_999_999,0),new(100_000_000,0),new(100_000_000,1),new(99_999_999,1)]};
        var square=new PatternPiece {Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(0,100)]};
        Assert.False(Value<bool>(Evaluate(edge,1),"EvaluationPassed"));
        Assert.False(Value<bool>(Evaluate(square,1,0),"EvaluationPassed"));
        Assert.False(Value<bool>(Evaluate(square,1),"EvaluationPassed"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LobedSourceCannotBypassTighterPhysicalBudget(bool reverse)
    {
        var fixture=typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.NonPublic|BindingFlags.Static)!;
        var piece=(PatternPiece)fixture.Invoke(null,[512,true])!;
        if(reverse)piece.Points.Reverse();var before=piece.Points.ToArray();
        var result=Evaluate(piece,1,.000000001,.000003,.000003);
        Assert.False(Value<bool>(result,"EvaluationPassed"));
        Assert.True(Value<bool>(result,"OriginalPreLocalChecksPassed"));
        Assert.False(Value<bool>(result,"SourceGridBudgetPassed"));
        Assert.Empty(Value<IReadOnlyList<Point2>>(result,"DiagnosticCandidate"));
        Assert.Equal(before,piece.Points);
    }

    [Fact]
    public void MarkedNotchesAndCancellationFailClosed()
    {
        var piece=new PatternPiece {Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(0,100)],
            Notches=[new NotchMark { VertexIndex=0,Position=new Point2(0,0) }]};
        var result=Evaluate(piece,1);
        Assert.False(Value<bool>(result,"EvaluationPassed"));Assert.Empty(Value<IReadOnlyList<Point2>>(result,"DiagnosticCandidate"));
        var method=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetClosedEvaluation",true)!
            .GetMethod("Evaluate",BindingFlags.NonPublic|BindingFlags.Static)!;
        using var c=new CancellationTokenSource();c.Cancel();
        var ex=Assert.Throws<TargetInvocationException>(()=>method.Invoke(null,[piece,1d,.001,.001,.001,c.Token]));
        Assert.IsAssignableFrom<OperationCanceledException>(ex.InnerException);
    }
}
