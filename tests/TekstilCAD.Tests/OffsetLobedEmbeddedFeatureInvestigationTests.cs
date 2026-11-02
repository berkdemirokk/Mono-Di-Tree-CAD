using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

/// <summary>Fixture-specific investigation; a pass is not feature or production approval.</summary>
public sealed class OffsetLobedEmbeddedFeatureInvestigationTests(ITestOutputHelper log)
{
    private static T Value<T>(object o,string name)=>(T)o.GetType().GetProperty(name)!.GetValue(o)!;
    private static object Evaluate(PatternPiece p)
    {
        var m=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetClosedEvaluation",true)!
            .GetMethod("Evaluate",BindingFlags.NonPublic|BindingFlags.Static)!;
        return m.Invoke(null,[p,1d,.00001,.00002,.00002,CancellationToken.None])!;
    }
    private static (bool Ok,string Reason) Measure(Point2[] source,Point2[] candidate,int a,int w,int b,double width,double depth)
    {
        var m=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetUserFeatureToleranceExperiment",true)!
            .GetMethod("TryVerify",BindingFlags.Static|BindingFlags.NonPublic)!;
        object?[] args=[source,candidate,a,w,b,width,depth,null,CancellationToken.None];
        bool ok=(bool)m.Invoke(null,args)!;return(ok,(string)args[7]!);
    }
    [Theory]
    [InlineData(false,6d,.5d)]
    [InlineData(true,6d,.5d)]
    [InlineData(false,4d,1d)]
    [InlineData(true,4d,1d)]
    public void MeasureSmallVInsertedIntoActualLobedFixture(bool reverse,double width,double depth)
    {
        var fixture=typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.NonPublic|BindingFlags.Static)!;
        var original=(PatternPiece)fixture.Invoke(null,[512,true])!;
        var p=PatternTools.InsertNotch(original,146,.5,width,depth);
        int a=147,w=148,b=149;
        if(reverse){p.Points.Reverse();(a,w,b)=(p.Points.Count-1-b,p.Points.Count-1-w,p.Points.Count-1-a);}
        var before=p.Points.ToArray();var evaluation=Evaluate(p);
        bool accepted=Value<bool>(evaluation,"EvaluationPassed");var reason=Value<string>(evaluation,"Reason");
        log.WriteLine($"reverse={reverse}; width={width}; depth={depth}; evidence={accepted}; reason={reason}");
        Assert.False(Value<bool>(evaluation,"FeaturesCertified"));Assert.False(Value<bool>(evaluation,"ProductionApproved"));
        Assert.Equal(before,p.Points);
        Assert.Equal(width==6,accepted);
        if(!accepted){Assert.Empty(Value<IReadOnlyList<Point2>>(evaluation,"DiagnosticCandidate"));return;}
        var candidate=Value<IReadOnlyList<Point2>>(evaluation,"DiagnosticCandidate").ToArray();
        var precise=Measure(before,candidate,a,w,b,.000001,.000001);
        var relaxed=Measure(before,candidate,a,w,b,2,2);
        static double Depth(Point2 p,Point2 q,Point2 r)
        {
            var x=r-p;var y=q-p;return Math.Abs(x.X*y.Y-x.Y*y.X)/Geometry.Distance(p,r);
        }
        var inputWidth=Geometry.Distance(before[a],before[b]);var outputWidth=Geometry.Distance(candidate[a],candidate[b]);
        var inputDepth=Depth(before[a],before[w],before[b]);var outputDepth=Depth(candidate[a],candidate[w],candidate[b]);
        log.WriteLine($"selected feature tolerance .000001mm={precise.Ok}, reason={precise.Reason}; 2mm={relaxed.Ok}, reason={relaxed.Reason}");
        log.WriteLine($"mouth width source={inputWidth:G17}mm candidate={outputWidth:G17}mm change={outputWidth-inputWidth:G17}mm; depth source={inputDepth:G17}mm candidate={outputDepth:G17}mm change={outputDepth-inputDepth:G17}mm");
        Assert.False(precise.Ok);Assert.True(relaxed.Ok,relaxed.Reason);
        Assert.InRange(outputWidth-inputWidth,-.166,-.165);
        Assert.InRange(outputDepth-inputDepth,-.014,-.013);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LargeOriginLobedCandidateStillRequiresOriginalGuards(bool reverse)
    {
        var fixture=typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.NonPublic|BindingFlags.Static)!;
        var source=(PatternPiece)fixture.Invoke(null,[512,true])!;
        source.Points=source.Points.Select(p=>new Point2(p.X+50_000_000,p.Y+50_000_000)).ToList();
        if(reverse)source.Points.Reverse();var before=source.Points.ToArray();var result=Evaluate(source);
        log.WriteLine($"large origin reverse={reverse}; passed={Value<bool>(result,"EvaluationPassed")}; reason={Value<string>(result,"Reason")}");
        Assert.False(Value<bool>(result,"EvaluationPassed"));
        Assert.Contains("not the exact nearest source grid point",Value<string>(result,"Reason"));
        Assert.False(Value<bool>(result,"ProductionApproved"));Assert.False(Value<bool>(result,"FeaturesCertified"));
        Assert.Equal(before,source.Points);
    }
}
