using TekstilCAD.Core;
using TekstilCAD.Grading;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class MidpointGradingRuleTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public void SplittingEdgeKeepsGradedMidpointOnGradedEdge(bool repeated, int index)
    {
        var piece = new PatternPiece { Closed = true, Points = repeated
            ? [new(0, 0), new(20, 0), new(0, 20), new(0, 0)]
            : [new(0, 0), new(20, 0), new(0, 20)] };
        var rule = new GradingRuleSet { SourcePieceId = piece.Id, SourceClosed = true,
            SourcePoints = [.. piece.Points], Deltas = repeated
                ? [new(0, 0), new(2, 0), new(0, 2), new(0, 0)]
                : [new(0, 0), new(2, 0), new(0, 2)] };
        var edited = PatternTools.InsertEdgeMidpoint(piece, index);
        var mapped = rule.WithEdgeMidpointFor(piece, edited, index)!;
        var graded = GradingEngine.Generate(edited, mapped.Deltas, 2, "L");
        var actualIndex = repeated && index == 3 ? 0 : index;
        var nextIndex = (actualIndex + 2) % graded.Points.Count;
        var a = graded.Points[actualIndex]; var b = graded.Points[nextIndex];
        Assert.Equal(new Point2((a.X + b.X) / 2, (a.Y + b.Y) / 2), graded.Points[actualIndex + 1]);
        Assert.Equal(piece.Points, rule.SourcePoints);
        Assert.Equal(piece.Points.Count, rule.Deltas.Count);
    }

    [Fact]
    public void StaleRuleOrDifferentEditIsNeverReassigned()
    {
        var piece = new PatternPiece { Closed = false, Points = [new(0, 0), new(10, 0)] };
        var rule = new GradingRuleSet { SourcePieceId = piece.Id, SourcePoints = [.. piece.Points], Deltas = [new(1, 0), new(3, 0)] };
        var edited = PatternTools.InsertEdgeMidpoint(piece, 0);
        Assert.NotNull(rule.WithEdgeMidpointFor(piece, edited, 0));
        edited.Points[1] = new(4, 0);
        Assert.Null(rule.WithEdgeMidpointFor(piece, edited, 0));
        piece.Points[1] = new(12, 0);
        Assert.Null(rule.WithEdgeMidpointFor(piece, edited, 0));
    }
}
