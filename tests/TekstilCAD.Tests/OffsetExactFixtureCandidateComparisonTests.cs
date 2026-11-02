using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Test-only path composition; it is not a production offset or feature certificate.</summary>
public sealed class OffsetExactFixtureCandidateComparisonTests
{
    private static (bool Ok, IReadOnlyList<Point2> Path, string Reason) CandidatePath(PatternPiece piece)
    {
        const double scale=1_000_000;
        var assembly=typeof(PatternTools).Assembly;
        var source=piece.Points.ToArray();
        Geometry.ValidateContour(source);
        var grid=source.Select(p=>new GridPoint(
            checked((long)Math.Round(p.X*scale,MidpointRounding.AwayFromZero)),
            checked((long)Math.Round(p.Y*scale,MidpointRounding.AwayFromZero)))).ToArray();
        if(grid.Distinct().Count()!=grid.Length)return (false,Array.Empty<Point2>(),"grid points collide");
        var turnGuard=assembly.GetType("TekstilCAD.Patterns.OffsetSourceGridTurnGuard",true)!
            .GetMethod("PreservesTurns",BindingFlags.Static|BindingFlags.NonPublic)!;
        if(!(bool)turnGuard.Invoke(null,[source,grid.Select(p=>(p.X,p.Y)).ToArray(),CancellationToken.None])!)
            return (false,Array.Empty<Point2>(),"source turn changes");
        var certificate=assembly.GetType("TekstilCAD.Patterns.OffsetIndexedSweepCertificate",true)!
            .GetMethod("TryCreate",BindingFlags.Static|BindingFlags.NonPublic)!;
        object?[] args=[grid,1_000_000L,null,null,CancellationToken.None];
        bool ok=(bool)certificate.Invoke(null,args)!;
        if(!ok)return (false,Array.Empty<Point2>(),(string)args[3]!);
        var path=((IReadOnlyList<GridPoint>)args[2]!).Select(p=>new Point2(p.X/scale,p.Y/scale)).ToArray();
        return (true,path,(string)args[3]!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameLobedFixtureProducesCandidatePathButProductionStillRejects(bool reverse)
    {
        // Invoke the existing fixture itself, not an approximation of its formula.
        var fixture=typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.NonPublic|BindingFlags.Static)!;
        var source=(PatternPiece)fixture.Invoke(null,[512,true])!;
        if(reverse)source.Points.Reverse();
        var before=source.Points.ToArray();var sourceId=source.Id;
        var result=CandidatePath(source);
        Assert.True(result.Ok,result.Reason);
        Assert.Equal(before.Length,result.Path.Count);
        var bounds=Geometry.Bounds(before);var actual=Geometry.Bounds(result.Path);
        Assert.InRange(bounds.MinX-actual.MinX,.999,1.01);
        Assert.InRange(bounds.MinY-actual.MinY,.999,1.01);
        Assert.InRange(actual.MaxX-bounds.MaxX,.999,1.01);
        Assert.InRange(actual.MaxY-bounds.MaxY,.999,1.01);
        Assert.True(Math.Abs(Geometry.SignedArea(result.Path))>Math.Abs(Geometry.SignedArea(before)));
        Assert.False(Geometry.HasSelfIntersections(result.Path));
        Assert.Throws<OffsetLocalDetailUnverifiedException>(()=>PatternTools.Offset(source,1));
        Assert.Equal(before,source.Points);Assert.Equal(sourceId,source.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneGridNotchIsRejectedWithoutReturningCandidate(bool reverse)
    {
        var source=new PatternPiece {Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(50.000002,100),
            new(50.000002,99.999999),new(50,99.999999),new(50,100),new(0,100)]};
        if(reverse)source.Points.Reverse();var before=source.Points.ToArray();
        var result=CandidatePath(source);
        Assert.False(result.Ok);Assert.Empty(result.Path);Assert.Equal(before,source.Points);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuantizationMustNotEraseAValidSourceTurn(bool reverse)
    {
        var source=new PatternPiece {Closed=true,Points=[new(0,0),new(100,0.0000004),new(200,0),new(200,100),new(0,100)]};
        if(reverse)source.Points.Reverse();var before=source.Points.ToArray();
        var result=CandidatePath(source);
        Assert.False(result.Ok);Assert.Equal("source turn changes",result.Reason);Assert.Empty(result.Path);
        Assert.Equal(before,source.Points);
    }

    [Fact]
    public void QuantizationMustNotMergeDistinctSourceVertices()
    {
        var source=new PatternPiece {Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(50.0000004,100),new(50,100),new(0,100)]};
        var before=source.Points.ToArray();var result=CandidatePath(source);
        Assert.False(result.Ok);Assert.Equal("grid points collide",result.Reason);Assert.Empty(result.Path);
        Assert.Equal(before,source.Points);
    }
}
