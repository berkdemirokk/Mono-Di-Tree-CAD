using Clipper2Lib;
using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class OffsetShadowCoverageTests
{
    private static List<Point2> ClipperOutput(List<Point2> points,double delta)
    {
        var path=new PathD(points.Select(p=>new PointD(p.X,p.Y)));
        var result=Clipper.InflatePaths(new PathsD {path},delta,JoinType.Miter,EndType.Polygon,2,6);
        return Assert.Single(result).Select(p=>new Point2(p.x,p.y)).ToList();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmoothContourComparisonReportsAggregateCoverage(bool reverse)
    {
        var points=Enumerable.Range(0,512).Select(i=>{var a=2*Math.PI*i/512;var r=1000+200*Math.Cos(3*a);return new Point2(r*Math.Cos(a),r*Math.Sin(a));}).ToList();
        if(reverse)points.Reverse();
        var result=OffsetShadowDiagnostic.Compare(points,1,ClipperOutput(points,1),.02);
        Assert.True(result.Compared);
        // This measures aggregate coverage only: a separate sharp-feature veto
        // can still make FullyCovered false, even below the length tolerance.
        Assert.True(result.AggregateWithinTolerance,$"uncovered {result.CandidateUncoveredMm}, {result.OutputUncoveredMm}; reason: {result.Reason}");
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollapsedRecessCannotBeCertified(bool reverse)
    {
        List<Point2> points=[new(0,0),new(100,0),new(100,100),new(52,100),new(52,20),new(48,20),new(48,100),new(0,100)];
        if(reverse)points.Reverse();
        var result=OffsetShadowDiagnostic.Compare(points,5,ClipperOutput(points,5),.02);
        Assert.False(result.Compared);
        Assert.False(result.FullyCovered);
        Assert.Null(result.AggregateWithinTolerance);
        Assert.Null(result.SharpFeatureVeto);
    }
    [Fact]
    public void IncorrectContourCannotBeReportedAsCovered()
    {
        List<Point2> square=[new(0,0),new(100,0),new(100,100),new(0,100)];
        var result=OffsetShadowDiagnostic.Compare(square,1,square,.001);
        Assert.True(result.Compared);
        Assert.False(result.FullyCovered);
    }
}
