using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;
using static TekstilCAD.Patterns.OffsetMiterSeparationDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetMiterSeparationDiagnosticTests
{
    [Theory]
    [InlineData(false,0L)]
    [InlineData(true,0L)]
    [InlineData(false,50000000000000L)]
    [InlineData(true,50000000000000L)]
    public void AnalyticRectangleHasSeparatedOppositeEdges(bool reverse,long origin)
    {
        List<GridPoint> points = [new(0,0),new(100,0),new(100,80),new(0,80)];
        if (reverse) points.Reverse();
        points = points.Select(p => new GridPoint(p.X+origin,p.Y-origin)).ToList();
        var before = points.ToArray();
        var result = Analyze(points,5,reverse ? -1 : 1);
        Assert.Equal(Finding.NonAdjacentPairsSeparated,result.Finding);
        Assert.Null(result.FirstEdge);
        Assert.Equal(before,points);
    }

    [Fact]
    public void DiagonalEdgesCanBeSeparatedByOrientationEvenWhenBoxesMeet()
    {
        List<GridPoint> diamond = [new(0,5),new(5,10),new(10,5),new(5,0)];
        Assert.Equal(Finding.NonAdjacentPairsSeparated,Analyze(diamond,1,-1).Finding);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CrossingCandidateIsNeverReportedSeparated(bool reverse)
    {
        List<GridPoint> bow = [new(0,0),new(100,100),new(0,100),new(100,0)];
        if (reverse) bow.Reverse();
        var result = Analyze(bow,1,reverse ? -1 : 1);
        Assert.Equal(Finding.Unknown,result.Finding);
    }

    [Fact]
    public void TouchingRecessAndBudgetExhaustionAreUnknown()
    {
        List<GridPoint> notch = [new(0,0),new(100,0),new(100,100),new(52,100),
            new(52,20),new(48,20),new(48,100),new(0,100)];
        Assert.Equal(Finding.Unknown,Analyze(notch,2,1).Finding);
        List<GridPoint> square = [new(0,0),new(10,0),new(10,10),new(0,10)];
        Assert.Equal(Finding.Unknown,Analyze(square,1,1,pairBudget:1).Finding);
        Assert.Equal(Finding.Unknown,Analyze(square,0,1).Finding);
        Assert.Equal(Finding.Unknown,Analyze([new(0,0),new(0,0),new(1,1)],1,1).Finding);
    }
}
