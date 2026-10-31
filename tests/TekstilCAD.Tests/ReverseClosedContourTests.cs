using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class ReverseClosedContourTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReversesNotchedContourAndMatchingIncrementsTogether(bool explicitClosure)
    {
        var piece = new PatternPiece { Points = [new(0,0), new(8,0), new(10,2), new(12,0), new(20,0), new(20,10), new(0,10)] };
        if (explicitClosure) piece.Points.Add(piece.Points[0]);
        var original = piece.Clone();
        var rule = new GradingRuleSet { SourcePieceId = piece.Id, SourceClosed = true, SourcePoints = [.. piece.Points],
            Deltas = piece.Points.Select((_, i) => new Point2(i, -i)).ToList() };
        if (explicitClosure) rule.Deltas[^1] = rule.Deltas[0];
        var originalDeltas = rule.Deltas.ToArray();

        var result = PatternTools.ReverseClosedContour(piece);
        result.Id = piece.Id; // The editor retains the source identity on replacement.
        var reversedRule = rule.ReversedClosedFor(piece)!;
        Assert.Equal(piece.Points[0], result.Points[0]);
        Assert.Equal(explicitClosure, result.Points[0] == result.Points[^1]);
        Assert.Equal(-Geometry.SignedArea(piece.Points), Geometry.SignedArea(result.Points));
        Assert.Equal(result.Points, reversedRule.SourcePoints);
        for (var i = 0; i < result.Points.Count; i++)
        {
            var oldIndex = piece.Points.FindIndex(p => p == result.Points[i]);
            Assert.Equal(originalDeltas[oldIndex], reversedRule.Deltas[i]);
        }
        Assert.Equal(original.Points, piece.Points);
        Assert.Equal(originalDeltas, rule.Deltas);
        Assert.Equal(piece.Points, PatternTools.ReverseClosedContour(result).Points);
        Assert.Equal(rule.Deltas, reversedRule.ReversedClosedFor(result)!.Deltas);
    }

    [Fact]
    public void RejectsOpenAndStaleRuleWithoutMutation()
    {
        var piece = new PatternPiece { Points = [new(0,0), new(20,0), new(0,10)] };
        var rule = new GradingRuleSet { SourcePieceId = piece.Id, SourceClosed = true, SourcePoints = [.. piece.Points], Deltas = [new(0,0), new(1,0), new(0,1)] };
        piece.Points[1] = new(21,0);
        Assert.Null(rule.ReversedClosedFor(piece));
        piece.Closed = false;
        Assert.Throws<ArgumentException>(() => PatternTools.ReverseClosedContour(piece));
        Assert.Equal(new Point2(21,0), piece.Points[1]);
    }

    [Fact]
    public void HistoryRestoresContourAndGradingAsOneChange()
    {
        var piece = new PatternPiece { Points = [new(0,0), new(20,0), new(20,10), new(0,10)] };
        var rule = new GradingRuleSet { SourcePieceId = piece.Id, SourceClosed = true, SourcePoints = [.. piece.Points], Deltas = [new(0,0), new(1,0), new(2,0), new(3,0)] };
        var before = new ProjectDocument { Pieces = [piece], GradingRules = [rule] };
        var history = new DocumentHistory(); history.Record(before);
        var after = before.Clone(); after.Pieces[0] = PatternTools.ReverseClosedContour(piece); after.GradingRules[0] = rule.ReversedClosedFor(piece)!;
        var undo = history.Undo(after)!;
        Assert.Equal(before.Pieces[0].Points, undo.Pieces[0].Points);
        Assert.Equal(before.GradingRules[0].Deltas, undo.GradingRules[0].Deltas);
        var redo = history.Redo(undo)!;
        Assert.Equal(after.Pieces[0].Points, redo.Pieces[0].Points);
        Assert.Equal(after.GradingRules[0].Deltas, redo.GradingRules[0].Deltas);
    }
}
