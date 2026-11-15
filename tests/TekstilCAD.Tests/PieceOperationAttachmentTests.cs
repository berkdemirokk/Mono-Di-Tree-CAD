using TekstilCAD.Core;
using static TekstilCAD.Tests.PieceOperationTestData;

namespace TekstilCAD.Tests;

/// <summary>Notch marks, grainline and grading rule travel with a piece or are reported, never silently lost.</summary>
public sealed class PieceOperationAttachmentTests
{
    private const string StaleRule = "Kayıtlı serileme kuralı bu parçanın eski konturuna ait kaldı; serilemeden önce kuralı gözden geçirin.";

    private static readonly Point2[] Deltas = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];

    /// <summary>120 x 80 rectangle with two notch marks, a vertical grainline at x = 30 and a matching grading rule.</summary>
    private static ProjectDocument Equipped()
    {
        var piece = Rect("Ön beden", 0, 0, 120, 80);
        piece.Notches = [new NotchMark { VertexIndex = 1, Position = piece.Points[1] }, new NotchMark { VertexIndex = 3, Position = piece.Points[3] }];
        piece.Grainline = new Grainline { Start = new(30, 10), End = new(30, 70), NeedsReview = true };
        var document = Doc(piece);
        document.GradingRules.Add(new GradingRuleSet { SourcePieceId = piece.Id, SourceClosed = true, SourcePoints = [.. piece.Points], Deltas = [.. Deltas] });
        return document;
    }

    private static void AssertNotchesOnTheirVertices(PatternPiece piece)
    {
        Assert.Equal([1, 3], piece.Notches!.Select(n => n.VertexIndex));
        foreach (var notch in piece.Notches) Assert.Equal(piece.Points[notch.VertexIndex], notch.Position);
    }

    [Fact]
    public void MoveCarriesNotchesGrainlineAndRule()
    {
        var document = Equipped();
        var id = document.Pieces[0].Id;
        var result = Run(document, d => PieceOperations.MoveBy(d, id, 10, 20));
        var moved = Find(result, id);
        AssertNotchesOnTheirVertices(moved);
        Assert.Equal(new Point2(40, 30), moved.Grainline!.Start);
        Assert.Equal(new Point2(40, 90), moved.Grainline.End);
        Assert.True(moved.Grainline.NeedsReview);
        Assert.Equal(moved.Points, result.Document!.GradingRules[0].SourcePoints);
        Assert.Equal(Deltas, result.Document.GradingRules[0].Deltas);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void MoveToCarriesThemToo()
    {
        var document = Equipped();
        var result = Run(document, d => PieceOperations.MoveTo(d, d.Pieces[0].Id, 100, 100));
        AssertNotchesOnTheirVertices(result.Piece!);
        Assert.Equal(new Point2(130, 110), result.Piece!.Grainline!.Start);
        Assert.Equal(result.Piece.Points, result.Document!.GradingRules[0].SourcePoints);
    }

    [Fact]
    public void RotationCarriesGrainlineNotchesAndTransformsRuleIncrements()
    {
        var document = Equipped();
        var result = Run(document, d => PieceOperations.Rotate(d, d.Pieces[0].Id, 90));
        var piece = result.Piece!;
        AssertNotchesOnTheirVertices(piece);
        // Grainline (30,10)-(30,70) about the centre (60,40), clockwise on screen.
        Assert.Equal(new Point2(90, 10), piece.Grainline!.Start);
        Assert.Equal(new Point2(30, 10), piece.Grainline.End);
        Assert.Equal(piece.Points, result.Document!.GradingRules[0].SourcePoints);
        // RotateLeft maps (x, y) to (-y, x).
        Assert.Equal([new Point2(0, 0), new Point2(0, 1), new Point2(-1, 1), new Point2(-1, 0)], result.Document.GradingRules[0].Deltas);
        Assert.Equal(["Serileme artışları parçayla birlikte dönüştürüldü; bedenleri kontrol edin."], result.Warnings);
    }

    [Fact]
    public void CounterClockwiseRotationUsesTheOtherIncrementMapping()
    {
        var document = Equipped();
        var result = Run(document, d => PieceOperations.Rotate(d, d.Pieces[0].Id, -90));
        // RotateRight maps (x, y) to (y, -x).
        Assert.Equal([new Point2(0, 0), new Point2(0, -1), new Point2(1, -1), new Point2(1, 0)], result.Document!.GradingRules[0].Deltas);
        AssertNotchesOnTheirVertices(result.Piece!);
    }

    [Fact]
    public void HalfTurnNegatesTheIncrements()
    {
        var document = Equipped();
        var result = Run(document, d => PieceOperations.Rotate(d, d.Pieces[0].Id, 180));
        Assert.Equal([new Point2(0, 0), new Point2(-1, 0), new Point2(-1, -1), new Point2(0, -1)], result.Document!.GradingRules[0].Deltas);
        Assert.Equal(new Point2(90, 70), result.Piece!.Grainline!.Start);
    }

    [Fact]
    public void MirrorVerticalFlipsGrainlineAndTheXIncrements()
    {
        var document = Equipped();
        var result = Run(document, d => PieceOperations.MirrorVertical(d, d.Pieces[0].Id));
        Assert.Equal(new Point2(90, 10), result.Piece!.Grainline!.Start);
        Assert.Equal(new Point2(90, 70), result.Piece.Grainline.End);
        Assert.Equal([new Point2(0, 0), new Point2(-1, 0), new Point2(-1, 1), new Point2(0, 1)], result.Document!.GradingRules[0].Deltas);
        AssertNotchesOnTheirVertices(result.Piece);
        Assert.Equal(result.Piece.Points, result.Document.GradingRules[0].SourcePoints);
    }

    [Fact]
    public void MirrorHorizontalFlipsGrainlineAndTheYIncrements()
    {
        var document = Equipped();
        var result = Run(document, d => PieceOperations.MirrorHorizontal(d, d.Pieces[0].Id));
        Assert.Equal(new Point2(30, 70), result.Piece!.Grainline!.Start);
        Assert.Equal(new Point2(30, 10), result.Piece.Grainline.End);
        Assert.Equal([new Point2(0, 0), new Point2(1, 0), new Point2(1, -1), new Point2(0, -1)], result.Document!.GradingRules[0].Deltas);
        AssertNotchesOnTheirVertices(result.Piece);
    }

    [Fact]
    public void ResizeMovesNotchesWithTheirVerticesScalesTheGrainlineAndLeavesTheRuleWithAWarning()
    {
        var document = Equipped();
        var result = Run(document, d => PieceOperations.Resize(d, d.Pieces[0].Id, 240, 160));
        var piece = result.Piece!;
        AssertNotchesOnTheirVertices(piece);
        Assert.Equal(new Point2(240, 0), piece.Notches![0].Position);
        Assert.Equal(new Point2(60, 20), piece.Grainline!.Start);
        Assert.Equal(new Point2(60, 140), piece.Grainline.End);
        Assert.True(piece.Grainline.NeedsReview);
        // The rule stays exactly as it was and no longer matches the geometry.
        Assert.Equal(document.GradingRules[0].SourcePoints, result.Document!.GradingRules[0].SourcePoints);
        Assert.Equal(["İplik yönü parçayla birlikte ölçeklendi ve gözden geçirilmek üzere işaretlendi.", StaleRule], result.Warnings);
    }

    [Fact]
    public void ResizeToTheSameSizeLeavesGrainlineRuleAndWarningsAlone()
    {
        var document = Equipped();
        var result = Run(document, d => PieceOperations.Resize(d, d.Pieces[0].Id, 120, 80));
        Assert.False(result.ChangesDocument);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void NeedsReviewStaysFalseThroughRigidOperations()
    {
        var document = Equipped();
        document.Pieces[0].Grainline!.NeedsReview = false;
        var result = Run(document, d => PieceOperations.Rotate(d, d.Pieces[0].Id, 90));
        Assert.False(result.Piece!.Grainline!.NeedsReview);
    }

    [Fact]
    public void ARuleThatAlreadyDidNotMatchIsKeptAndReportedOnMove()
    {
        var document = Equipped();
        document.GradingRules[0].SourcePoints[0] = new Point2(1, 1);
        var result = Run(document, d => PieceOperations.MoveBy(d, d.Pieces[0].Id, 10, 0));
        Assert.Equal([StaleRule], result.Warnings);
        Assert.Equal(document.GradingRules[0].SourcePoints, result.Document!.GradingRules[0].SourcePoints);
    }

    [Fact]
    public void ARuleThatAlreadyDidNotMatchIsKeptAndReportedOnRotate()
    {
        var document = Equipped();
        document.GradingRules[0].SourcePoints[0] = new Point2(1, 1);
        var result = Run(document, d => PieceOperations.Rotate(d, d.Pieces[0].Id, 90));
        Assert.Equal([StaleRule], result.Warnings);
        Assert.Equal(document.GradingRules[0].Deltas, result.Document!.GradingRules[0].Deltas);
    }

    [Fact]
    public void RulesOfOtherPiecesAreNeverTouched()
    {
        var document = Equipped();
        var other = Rect("Kol", 500, 0, 10, 10);
        document.Pieces.Add(other);
        document.GradingRules.Add(new GradingRuleSet { SourcePieceId = other.Id, SourceClosed = true, SourcePoints = [.. other.Points], Deltas = [.. Deltas] });
        var result = Run(document, d => PieceOperations.Rotate(d, d.Pieces[0].Id, 90));
        Assert.Equal(other.Points, result.Document!.GradingRules.Single(r => r.SourcePieceId == other.Id).SourcePoints);
    }

    [Fact]
    public void NotchMarksThatDoNotMatchTheirVerticesStopAMove()
    {
        var document = Equipped();
        document.Pieces[0].Notches![0].Position = new Point2(5, 5);
        var result = PieceOperations.MoveBy(document, document.Pieces[0].Id, 1, 1);
        Assert.False(result.Succeeded);
        Assert.Equal(PieceOperationError.Precision, result.ErrorCode);
        Assert.Contains("İşaret kaynak konturla eşleşmiyor", result.ErrorDetail);
    }

    [Fact]
    public void NotchMarksThatDoNotMatchTheirVerticesStopARotation()
    {
        var document = Equipped();
        document.Pieces[0].Notches![0].Position = new Point2(5, 5);
        Assert.False(PieceOperations.Rotate(document, document.Pieces[0].Id, 90).Succeeded);
    }

    [Fact]
    public void ADegenerateGrainlineStopsTheOperationInsteadOfBeingDropped()
    {
        var document = Equipped();
        document.Pieces[0].Grainline = new Grainline { Start = new(30, 10), End = new(30, 10) };
        var result = PieceOperations.MoveBy(document, document.Pieces[0].Id, 1, 1);
        Assert.False(result.Succeeded);
        Assert.Equal(PieceOperationError.Precision, result.ErrorCode);
    }

    [Theory]
    [InlineData("move")]
    [InlineData("moveto")]
    [InlineData("resize")]
    [InlineData("rotate")]
    [InlineData("mirrorv")]
    [InlineData("mirrorh")]
    [InlineData("duplicate")]
    [InlineData("delete")]
    [InlineData("rename")]
    public void EquippedPieceUndoesInOneStepForEveryOperation(string name)
    {
        Func<ProjectDocument, PieceOperationResult> operation = name switch
        {
            "move" => d => PieceOperations.MoveBy(d, d.Pieces[0].Id, 3, 4),
            "moveto" => d => PieceOperations.MoveTo(d, d.Pieces[0].Id, 7, 8),
            "resize" => d => PieceOperations.Resize(d, d.Pieces[0].Id, 60, 40),
            "rotate" => d => PieceOperations.Rotate(d, d.Pieces[0].Id, 90),
            "mirrorv" => d => PieceOperations.MirrorVertical(d, d.Pieces[0].Id),
            "mirrorh" => d => PieceOperations.MirrorHorizontal(d, d.Pieces[0].Id),
            "duplicate" => d => PieceOperations.Duplicate(d, d.Pieces[0].Id),
            "delete" => d => PieceOperations.Delete(d, d.Pieces[0].Id),
            _ => d => PieceOperations.Rename(d, d.Pieces[0].Id, "Yeni")
        };
        AssertUndoRoundTrip(Equipped(), operation);
    }
}
