using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public class ReverseGradingRuleTests
{
    [Fact]
    public void ReversesMatchingRuleWithItsPointsWithoutMutatingOriginal()
    {
        var piece = new PatternPiece { Closed = false, Points = [new(0, 0), new(10, 2), new(20, 0)] };
        var rule = new GradingRuleSet { SourcePieceId = piece.Id, SourcePoints = [.. piece.Points], Deltas = [new(1, 2), new(3, 4), new(5, 6)] };
        var reversed = rule.ReversedFor(piece)!;
        Assert.Equal(piece.Points.AsEnumerable().Reverse(), reversed.SourcePoints);
        Assert.Equal(rule.Deltas.AsEnumerable().Reverse(), reversed.Deltas);
        Assert.Equal(new Point2(1, 2), rule.Deltas[0]);
        Assert.Equal(rule.SourcePoints, reversed.ReversedFor(new PatternPiece { Id = piece.Id, Closed = false, Points = [.. reversed.SourcePoints] })!.SourcePoints);
    }

    [Fact]
    public void DoesNotReassignStaleOrUnrelatedRule()
    {
        var piece = new PatternPiece { Closed = false, Points = [new(0, 0), new(10, 0)] };
        var rule = new GradingRuleSet { SourcePieceId = piece.Id, SourcePoints = [.. piece.Points], Deltas = [new(1, 0), new(2, 0)] };
        piece.Points[1] = new(11, 0);
        Assert.Null(rule.ReversedFor(piece));
        piece.Points[1] = new(10, 0); piece.Closed = true;
        Assert.Null(rule.ReversedFor(piece));
        piece.Closed = false; rule.Deltas.RemoveAt(0);
        Assert.Null(rule.ReversedFor(piece));
    }
}
