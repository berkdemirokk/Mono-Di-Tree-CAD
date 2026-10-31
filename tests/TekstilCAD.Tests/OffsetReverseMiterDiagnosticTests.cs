using Clipper2Lib;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using Xunit.Abstractions;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public class OffsetReverseMiterDiagnosticTests(ITestOutputHelper output)
{
    private static GridPoint[] Square(long origin=0)=>[new(origin,origin),new(origin+10,origin),new(origin+10,origin+10),new(origin,origin+10)];
    [Fact]
    public void SameSquareHasZeroSampleDistanceAndRightAngleMiterRatio()
    {
        var path=Square();var result=OffsetReverseMiterDiagnostic.Analyze(path,path,16);
        Assert.Equal(16,result.WorstFindings.Count);Assert.All(result.WorstFindings,f=>Assert.Equal(0,f.DistanceGrid));
        Assert.All(result.WorstFindings,f=>Assert.Equal(Math.Sqrt(2),f.SampleStartMiterRatio,12));
    }
    [Fact]
    public void DisplacedCornerIdentifiesWorstRecoveredVertexAndNearestSourceEndpoint()
    {
        var source=Square();var recovered=Square();recovered[2]=new(11,11);
        var report=OffsetReverseMiterDiagnostic.Analyze(source,recovered,16);
        var worst=report.WorstFindings.First();
        Assert.Equal("recovered-to-source",worst.Direction);Assert.Equal(2,worst.SampleEdge);Assert.Equal(0,worst.SampleFraction);
        Assert.Equal(1,worst.NearestEdge);Assert.Equal(1,worst.NearestFraction);Assert.Equal(Math.Sqrt(2),worst.DistanceGrid,12);
        Assert.False(worst.SegmentsIntersect);
    }
    [Fact]
    public void IntegerSubtractionRetainsDiagnosticAtFarOrigin()
    {
        var source=Square();var recovered=Square();recovered[2]=new(11,11);
        var local=OffsetReverseMiterDiagnostic.Analyze(source,recovered,16).WorstFindings[0];
        const long shift=50_000_000_000_000;
        GridPoint Translate(GridPoint p)=>new(p.X+shift,p.Y-shift);
        var far=OffsetReverseMiterDiagnostic.Analyze(source.Select(Translate).ToArray(),recovered.Select(Translate).ToArray(),16).WorstFindings[0];
        Assert.Equal(local.Direction,far.Direction);Assert.Equal(local.SampleEdge,far.SampleEdge);Assert.Equal(local.NearestEdge,far.NearestEdge);
        Assert.Equal(local.DistanceGrid,far.DistanceGrid);Assert.Equal(local.SegmentsIntersect,far.SegmentsIntersect);
    }
    [Fact]
    public void SharpReversalExposesMiterRatioBeyondTwoWithoutApprovingEitherPath()
    {
        GridPoint[] path=[new(0,0),new(10,0),new(0,1),new(0,10)];
        var report=OffsetReverseMiterDiagnostic.Analyze(path,path,16);
        var sharp=report.WorstFindings.First(f=>f.Direction=="source-to-recovered" && f.SampleEdge==1 && f.SampleFraction==0);
        Assert.True(sharp.SampleStartMiterRatio>2);
        Assert.Equal(0,sharp.DistanceGrid);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualLobedClipperInverseReportsWorstEdgesAndMiterGeometry(bool reverse)
    {
        const long grid=1_000_000;
        var source=Enumerable.Range(0,512).Select(i=>
        {
            var a=Math.Tau*i/512;var radius=1000+200*Math.Cos(3*a);
            return new GridPoint((long)Math.Round(radius*Math.Cos(a)*grid,MidpointRounding.AwayFromZero),
                (long)Math.Round(radius*Math.Sin(a)*grid,MidpointRounding.AwayFromZero));
        }).ToArray();
        if(reverse)Array.Reverse(source);
        var path=new Path64(source.Select(p=>new Point64(p.X,p.Y)));
        var forward=Assert.Single(Clipper.InflatePaths(new Paths64 { path },grid,JoinType.Miter,EndType.Polygon,2));
        var inverse=Assert.Single(Clipper.InflatePaths(new Paths64 { forward },-grid,JoinType.Miter,EndType.Polygon,2));
        var recovered=inverse.Select(p=>new GridPoint(p.X,p.Y)).ToArray();
        var report=OffsetReverseMiterDiagnostic.Analyze(source,recovered,8);
        Assert.True(report.WorstFindings[0].DistanceGrid>8);
        Assert.All(report.WorstFindings,f=>Assert.True(double.IsFinite(f.DistanceGrid)));
        foreach(var f in report.WorstFindings)
            output.WriteLine($"reverse={reverse} direction={f.Direction} sample-edge={f.SampleEdge} sample-t={f.SampleFraction:G5} nearest-edge={f.NearestEdge} nearest-t={f.NearestFraction:G5} distance-grid={f.DistanceGrid:G17} intersects={f.SegmentsIntersect} sample-miters={f.SampleStartMiterRatio:G8},{f.SampleEndMiterRatio:G8} nearest-miters={f.NearestStartMiterRatio:G8},{f.NearestEndMiterRatio:G8}");
        var shortEdges=Enumerable.Range(0,forward.Count).Select(i=>
        {
            var a=forward[i];var b=forward[(i+1)%forward.Count];
            var dx=(double)(b.X-a.X);var dy=(double)(b.Y-a.Y);
            return (Index:i,Length:Math.Sqrt(dx*dx+dy*dy));
        }).Where(e=>e.Length<=2).ToArray();
        Assert.NotEmpty(shortEdges);
        foreach(var edge in shortEdges)output.WriteLine($"reverse={reverse} forward-short-edge={edge.Index} length-grid={edge.Length:G17}");
        var worst=report.WorstFindings[0];
        if(worst.Direction=="recovered-to-source" && worst.SampleFraction==0)
        {
            var p=inverse[worst.SampleEdge];var nearest=-1;double best=double.PositiveInfinity,nearestT=0;
            for(int i=0;i<forward.Count;i++)
            {
                var a=forward[i];var b=forward[(i+1)%forward.Count];
                double dx=(double)(b.X-a.X),dy=(double)(b.Y-a.Y),x=(double)(p.X-a.X),y=(double)(p.Y-a.Y);
                var length=dx*dx+dy*dy;if(length==0)continue;
                var t=Math.Clamp((x*dx+y*dy)/length,0,1);var ex=x-t*dx;var ey=y-t*dy;var squared=ex*ex+ey*ey;
                if(squared<best) { best=squared;nearest=i;nearestT=t; }
            }
            output.WriteLine($"reverse={reverse} inverse-worst-vertex={worst.SampleEdge} nearest-forward-edge={nearest} forward-t={nearestT:G17} distance-grid={Math.Sqrt(best):G17}");
        }
        // Miter ratios describe these recorded corners, not proof of causation or safe output.
    }
    [Fact]
    public void RejectsInvalidInputsAndCancellation()
    {
        var square=Square();Assert.Throws<ArgumentException>(()=>OffsetReverseMiterDiagnostic.Analyze(square,square,0));
        var degenerate=Square();degenerate[1]=degenerate[0];Assert.Throws<ArgumentException>(()=>OffsetReverseMiterDiagnostic.Analyze(degenerate,square));
        using var c=new CancellationTokenSource();c.Cancel();Assert.ThrowsAny<OperationCanceledException>(()=>OffsetReverseMiterDiagnostic.Analyze(square,square,16,c.Token));
    }
}
