using TekstilCAD.Core;
using TekstilCAD.Grading;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DerivedGradingRuleTests
{
    [Fact]
    public void DerivedRulePreservesBitsAndOwnsItsLists()
    {
        var piece = new PatternPiece { Points = [new(-0.0, 0), new(10, 0), new(0, 10)] };
        var deltas = new List<Point2> { new(-0.0, 2), new(1, 0), new(0, 1) };
        var rule = GradingEngine.CreateDerivedRule(piece, deltas);
        Assert.Equal(piece.Id, rule.SourcePieceId);
        Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(rule.Deltas[0].X));
        piece.Points[0] = new(5, 5); deltas[0] = new(4, 4);
        Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(rule.SourcePoints[0].X));
        Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(rule.Deltas[0].X));
    }

    [Fact]
    public void RepeatedClosureRequiresBitIdenticalCoordinatesAndIncrements()
    {
        var piece = new PatternPiece { Closed = true, Points = [new(0, 0), new(10, 0), new(0, 10), new(0, 0)] };
        var deltas = new List<Point2> { new(0, 0), new(1, 0), new(0, 1), new(-0.0, 0) };
        Assert.Throws<ArgumentException>(() => GradingEngine.CreateDerivedRule(piece, deltas));
        deltas[^1] = deltas[0];
        Assert.Equal(4, GradingEngine.CreateDerivedRule(piece, deltas).Deltas.Count);
        piece.Points[^1] = new(-0.0, 0);
        Assert.Throws<ArgumentException>(() => GradingEngine.CreateDerivedRule(piece, deltas));
    }

    [Fact]
    public void CancellationLeavesInputsUntouched()
    {
        var piece = new PatternPiece { Points = [new(0, 0), new(10, 0)] };
        var deltas = new List<Point2> { new(1, 0), new(1, 0) };
        using var source = new CancellationTokenSource(); source.Cancel();
        Assert.Throws<OperationCanceledException>(() => GradingEngine.CreateDerivedRule(piece, deltas, source.Token));
        Assert.Equal(new Point2(0, 0), piece.Points[0]); Assert.Equal(new Point2(1, 0), deltas[0]);
    }
    [Fact]
    public void DerivedRuleSurvivesProjectSaveAndOpen()
    {
        var piece = new PatternPiece { Points = [new(0, 0), new(10, 0), new(0, 10)] };
        var rule = GradingEngine.CreateDerivedRule(piece, [new(0, 0), new(1, 0), new(0, 1)]);
        var document = new ProjectDocument { Pieces = [piece], GradingRules = [rule] };
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".tcad");
        try
        {
            ProjectStorage.Save(document, path);
            Assert.True(DocumentSession.ContentEquals(document, ProjectStorage.Load(path)));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

}
