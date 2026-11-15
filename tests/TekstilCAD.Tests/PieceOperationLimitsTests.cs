using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using static TekstilCAD.Tests.PieceOperationTestData;

namespace TekstilCAD.Tests;

public sealed class PieceOperationLimitsTests
{
    [Fact]
    public void LimitConstantsEqualTheStorageLimits()
    {
        Assert.Equal(ProjectStorage.MaximumPieces, PieceOperations.MaximumPieces);
        Assert.Equal(ProjectStorage.MaximumPointsPerPiece, PieceOperations.MaximumPointsPerPiece);
        Assert.Equal(ProjectStorage.MaximumTotalPoints, PieceOperations.MaximumTotalPoints);
        Assert.Equal(Geometry.MaxCoordinate, 1e8);
    }

    private static ProjectDocument ManyPieces(int count)
    {
        var document = new ProjectDocument();
        for (var i = 0; i < count; i++) document.Pieces.Add(Rect($"P{i}", i * 2, 0, 1, 1));
        return document;
    }

    /// <summary>Open polylines of 10,000 points each; contents are irrelevant to the counters.</summary>
    private static PatternPiece Long(string name, int count) => new()
    {
        Name = name, Closed = false, Points = Enumerable.Range(0, count).Select(i => new Point2(i, i % 2)).ToList()
    };

    [Fact]
    public void TheTenThousandthPieceCanStillBeAdded()
    {
        var result = Run(ManyPieces(9_999), d => PieceOperations.AddRectangle(d, 1, 1));
        Assert.Equal(10_000, result.Document!.Pieces.Count);
    }

    [Fact]
    public void AddingBeyondTenThousandPiecesIsRefused()
    {
        var document = ManyPieces(10_000);
        const string message = "Projede en çok 10.000 parça olabilir; yeni parça eklenmedi. Önce bazı parçaları silin.";
        AssertRefused(PieceOperations.AddRectangle(document, 1, 1), PieceOperationError.LimitExceeded, message);
        AssertRefused(PieceOperations.AddLine(document, 1), PieceOperationError.LimitExceeded, message);
    }

    [Fact]
    public void DuplicatingBeyondTenThousandPiecesIsRefused()
    {
        var document = ManyPieces(10_000);
        AssertRefused(PieceOperations.Duplicate(document, document.Pieces[0].Id), PieceOperationError.LimitExceeded,
            "Projede en çok 10.000 parça olabilir; parça çoğaltılmadı. Önce bazı parçaları silin.");
    }

    [Fact]
    public void ExactlyTwoHundredThousandPointsAreStillAccepted()
    {
        var document = new ProjectDocument();
        for (var i = 0; i < 19; i++) document.Pieces.Add(Long($"U{i}", 10_000));
        document.Pieces.Add(Long("Son", 9_996));
        var result = Run(document, d => PieceOperations.AddRectangle(d, 5, 5));
        Assert.Equal(200_000, result.Document!.Pieces.Sum(p => p.Points.Count));
    }

    [Fact]
    public void OnePointOverTwoHundredThousandIsRefused()
    {
        var document = new ProjectDocument();
        for (var i = 0; i < 19; i++) document.Pieces.Add(Long($"U{i}", 10_000));
        document.Pieces.Add(Long("Son", 9_997));
        AssertRefused(PieceOperations.AddRectangle(document, 5, 5), PieceOperationError.LimitExceeded,
            "Projede en çok 200.000 nokta olabilir; yeni parça eklenmedi. Önce bazı parçaları silin.");
    }

    [Fact]
    public void DuplicatingALongPieceBeyondThePointLimitIsRefused()
    {
        var document = new ProjectDocument();
        for (var i = 0; i < 20; i++) document.Pieces.Add(Long($"U{i}", 10_000));
        AssertRefused(PieceOperations.Duplicate(document, document.Pieces[0].Id), PieceOperationError.LimitExceeded,
            "Projede en çok 200.000 nokta olabilir; parça çoğaltılmadı. Önce bazı parçaları silin.");
    }

    [Fact]
    public void ALongPieceCanBeMovedAndKeepsAllItsPoints()
    {
        var piece = Long("Uzun", 10_000);
        var result = Run(Doc(piece), d => PieceOperations.MoveBy(d, piece.Id, 5, 5));
        Assert.Equal(10_000, result.Piece!.Points.Count);
        Assert.Equal(new Point2(5, 5), result.Piece.Points[0]);
    }

    [Fact]
    public void RectanglesOfTheLargestAcceptedSizeValidate()
    {
        var result = Run(new ProjectDocument(), d => PieceOperations.AddRectangle(d, 1e8, 1e8, new Point2(0, 0)));
        Assert.Equal(new Point2(1e8, 1e8), result.Piece!.Points[2]);
    }

    [Fact]
    public void TheSmallestAcceptedRectangleValidatesAsAContour()
    {
        var result = Run(new ProjectDocument(), d => PieceOperations.AddRectangle(d, 0.0010001, 0.0010001));
        Geometry.ValidateContour(result.Piece!.Points);
    }
}
