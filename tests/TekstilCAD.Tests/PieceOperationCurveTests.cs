using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using static TekstilCAD.Tests.PieceOperationTestData;

namespace TekstilCAD.Tests;

public sealed class PieceOperationCurveTests
{
    /// <summary>An open piece made of a line and a cubic Bézier; its points are the exact preview sample the validator demands.</summary>
    public static PatternPiece CurvedPiece(string name)
    {
        List<CurveSegment> curves =
        [
            new CurveSegment.Line(new(0, 0), new(10, 0)),
            new CurveSegment.CubicBezier(new(10, 0), new(20, 0), new(30, 10), new(40, 10))
        ];
        return new PatternPiece { Name = name, Closed = false, Curves = curves, Points = ParametricContour.Sample(curves) };
    }

    [Fact]
    public void TheFixtureIsAValidProjectPiece()
    {
        ProjectStorage.Validate(Doc(CurvedPiece("Eğri")));
    }

    [Fact]
    public void EditingOnlyThePointsOfACurvePieceIsRejectedByTheValidator()
    {
        // This is why the operations never move Points alone.
        var piece = CurvedPiece("Eğri");
        piece.Points = piece.Points.Select(p => new Point2(p.X + 5, p.Y)).ToList();
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Validate(Doc(piece)));
    }

    [Theory]
    [InlineData(5, 7)]
    [InlineData(-3, 0.5)]
    [InlineData(0.5, 0.25)]
    [InlineData(1000, -1000)]
    public void MovingACurvePieceShiftsEveryControlPointAndRegeneratesThePreview(double dx, double dy)
    {
        var piece = CurvedPiece("Eğri");
        var result = Run(Doc(piece), d => PieceOperations.MoveBy(d, piece.Id, dx, dy));
        var moved = result.Piece!;
        Assert.NotNull(moved.Curves);
        var line = Assert.IsType<CurveSegment.Line>(moved.Curves![0]);
        Assert.Equal(new Point2(dx, dy), line.Start);
        Assert.Equal(new Point2(10 + dx, dy), line.End);
        var bezier = Assert.IsType<CurveSegment.CubicBezier>(moved.Curves[1]);
        Assert.Equal(new Point2(40 + dx, 10 + dy), bezier.P3);
        Assert.True(ParametricContour.MatchesPreview(moved));
        Assert.False(moved.Closed);
    }

    [Fact]
    public void MovingACurvePieceBackRestoresItsCurves()
    {
        var piece = CurvedPiece("Eğri");
        var document = Doc(piece);
        var there = Run(document, d => PieceOperations.MoveBy(d, piece.Id, 12, 34));
        var back = Run(there.Document!, d => PieceOperations.MoveBy(d, piece.Id, -12, -34));
        Assert.True(DocumentSession.ContentEquals(document, back.Document!));
    }

    [Fact]
    public void MoveToPlacesTheBoundingBoxOfACurvePiece()
    {
        var piece = CurvedPiece("Eğri");
        var result = Run(Doc(piece), d => PieceOperations.MoveTo(d, piece.Id, 100, 200));
        var box = Geometry.Bounds(result.Piece!.Points);
        Assert.Equal(100, box.MinX);
        Assert.Equal(200, box.MinY);
    }

    [Fact]
    public void DuplicatingACurvePieceCopiesAndMovesItsCurves()
    {
        var piece = CurvedPiece("Eğri");
        var result = Run(Doc(piece), d => PieceOperations.Duplicate(d, piece.Id, 20, 30));
        var copy = result.Piece!;
        Assert.NotEqual(piece.Id, copy.Id);
        Assert.Equal("Eğri kopya", copy.Name);
        Assert.Equal(new Point2(20, 30), Assert.IsType<CurveSegment.Line>(copy.Curves![0]).Start);
        Assert.True(ParametricContour.MatchesPreview(copy));
        // The source keeps its own curves.
        Assert.Equal(new Point2(0, 0), Assert.IsType<CurveSegment.Line>(result.Document!.Pieces[0].Curves![0]).Start);
    }

    [Fact]
    public void ACurvePieceWithNotchMarksIsNotMovedOrDuplicated()
    {
        var piece = CurvedPiece("Eğri");
        piece.Notches = [new NotchMark { VertexIndex = 0, Position = piece.Points[0] }];
        var document = Doc(piece);
        ProjectStorage.Validate(document);
        const string message = "“Eğri” hem yay/eğri hem çentik işareti içeriyor; işaretlerin yerinde kalacağı garanti edilemediği için taşıma veya çoğaltma uygulanmadı.";
        AssertRefused(PieceOperations.MoveBy(document, piece.Id, 5, 5), PieceOperationError.Unsupported, message);
        AssertRefused(PieceOperations.MoveTo(document, piece.Id, 5, 5), PieceOperationError.Unsupported, message);
        AssertRefused(PieceOperations.Duplicate(document, piece.Id), PieceOperationError.Unsupported, message);
    }

    [Fact]
    public void RenamingACurvePieceKeepsItsCurves()
    {
        var piece = CurvedPiece("Eğri");
        var result = Run(Doc(piece), d => PieceOperations.Rename(d, piece.Id, "Yeni ad"));
        Assert.Equal(piece.Curves, result.Piece!.Curves);
    }

    [Fact]
    public void DeletingACurvePieceRemovesItWholly()
    {
        var piece = CurvedPiece("Eğri");
        var result = Run(Doc(piece, Rect("A", 0, 0, 5, 5)), d => PieceOperations.Delete(d, piece.Id));
        Assert.Single(result.Document!.Pieces);
    }

    [Fact]
    public void AnArcPieceEitherMovesWithValidCurvesOrIsRefusedAndNothingChanges()
    {
        // Arc end points come from cos/sin, so a shifted arc may no longer join its neighbour bit for bit.
        var arc = new CurveSegment.Arc(new(0, 0), 10, 0, Math.PI / 2);
        List<CurveSegment> curves = [arc, new CurveSegment.Line(arc.PointAt(1), new(0, 30))];
        var piece = new PatternPiece { Name = "Yay", Closed = false, Curves = curves, Points = ParametricContour.Sample(curves) };
        var document = Doc(piece);
        ProjectStorage.Validate(document);
        foreach (var dx in new[] { 1d, 3d, 7.5, 100d, 12345.678 })
        {
            var result = PieceOperations.MoveBy(document, piece.Id, dx, 2 * dx);
            if (result.Succeeded)
            {
                ProjectStorage.Validate(result.Document!);
                Assert.True(ParametricContour.MatchesPreview(result.Piece!));
            }
            else
            {
                Assert.Equal(PieceOperationError.Precision, result.ErrorCode);
                Assert.Null(result.Document);
                Assert.False(string.IsNullOrWhiteSpace(result.Error));
            }
        }
    }

    [Fact]
    public void CurveMoveUndoesInOneStep()
    {
        var piece = CurvedPiece("Eğri");
        AssertUndoRoundTrip(Doc(piece, Rect("A", 0, 0, 5, 5)), d => PieceOperations.MoveBy(d, piece.Id, 9, 9));
    }
}
