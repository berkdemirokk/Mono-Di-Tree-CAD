using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public class TranslatedGradingRuleTests
{
    [Fact]
    public void MatchingRuleFollowsTranslationWithoutChangingIncrements()
    {
        var source = new PatternPiece { Points = [new(0, 0), new(20, 0), new(0, 10)] };
        var rule = new GradingRuleSet { SourcePieceId = source.Id, SourceClosed = true,
            SourcePoints = [.. source.Points], Deltas = [new(1, 2), new(3, 4), new(5, 6)] };
        var points = Geometry.Translate(source.Points, 10, -5);
        var moved = rule.TranslatedFor(source, points, 10, -5)!;
        Assert.Equal(points, moved.SourcePoints);
        Assert.Equal(rule.Deltas, moved.Deltas);
        Assert.Equal(source.Points, rule.SourcePoints);
        Assert.NotSame(rule.Deltas, moved.Deltas);
    }

    [Fact]
    public void StaleRuleAndUnrelatedEditAreNotReassigned()
    {
        var source = new PatternPiece { Closed = false, Points = [new(0, 0), new(10, 0)] };
        var rule = new GradingRuleSet { SourcePieceId = source.Id, SourcePoints = [.. source.Points], Deltas = [new(1, 0), new(2, 0)] };
        var points = Geometry.Translate(source.Points, 1, 0);
        points[1] = new(12, 0);
        Assert.Null(rule.TranslatedFor(source, points, 1, 0));
        source.Points[1] = new(11, 0);
        Assert.Null(rule.TranslatedFor(source, Geometry.Translate(source.Points, 1, 0), 1, 0));
    }
}
