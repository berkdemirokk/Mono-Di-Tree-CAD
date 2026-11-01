using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public class GradeDraftTranslationProofTests
{
    [Fact]
    public void GrainlineMustShareExactAnchorTranslation()
    {
        Point2[] before=[new(0,0),new(1,1),new(1,5)];
        Point2[] after=[new(1,0),new(2,1),new(2,5)];
        Assert.True(GradingRuleSet.IsExactPointTranslation(before,after));
        after[2]=new(double.BitIncrement(2),5);
        Assert.False(GradingRuleSet.IsExactPointTranslation(before,after));
        after[2]=before[2];
        Assert.False(GradingRuleSet.IsExactPointTranslation(before,after));
    }
    [Fact]
    public void AcceptsOnlyExactTranslationOfMatchingRuleAndSource()
    {
        var piece = new PatternPiece { Closed = false, Points = [new(0, 0), new(10, 4)] };
        var baseline = new GradingRuleSet { SourcePieceId = piece.Id, SourcePoints = [.. piece.Points], Deltas = [new(1, 2), new(3, 4)] };
        var moved = piece.Clone(); moved.Points = [new(1, 2), new(11, 6)];
        var current = baseline.Clone(); current.SourcePoints = [.. moved.Points];
        Assert.True(baseline.IsExactTranslationTo(current, moved));
        current.Deltas[1] = new(3, 5);
        Assert.False(baseline.IsExactTranslationTo(current, moved));
        current.Deltas[1] = baseline.Deltas[1];
        moved.Points[1] = new(12, 6);
        Assert.False(baseline.IsExactTranslationTo(current, moved));
        current.SourcePoints[1] = moved.Points[1];
        Assert.False(baseline.IsExactTranslationTo(current, moved));
    }

    [Fact]
    public void RejectsEvenOneBitOfRelativeShapeChange()
    {
        var piece = new PatternPiece { Closed = false, Points = [new(0, 0), new(10, 0)] };
        var baseline = new GradingRuleSet { SourcePieceId = piece.Id, SourcePoints = [.. piece.Points], Deltas = [new(0, 0), new(0, 0)] };
        var moved = piece.Clone(); moved.Points = [new(1, 0), new(double.BitIncrement(11), 0)];
        var current = baseline.Clone(); current.SourcePoints = [.. moved.Points];
        Assert.False(baseline.IsExactTranslationTo(current, moved));
    }
}
