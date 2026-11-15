using TekstilCAD.Core;
using static TekstilCAD.Tests.PieceOperationTestData;

namespace TekstilCAD.Tests;

public sealed class PieceMirrorTests
{
    [Fact]
    public void VerticalAxisSwapsLeftAndRight()
    {
        var document = OneRect();
        var result = Run(document, d => PieceOperations.MirrorVertical(d, d.Pieces[0].Id));
        Assert.Equal(Pts((120, 0), (0, 0), (0, 80), (120, 80)), result.Piece!.Points);
        Assert.Equal("“Ön beden” dikey eksende (sağ-sol) aynalandı.", result.Summary);
    }

    [Fact]
    public void HorizontalAxisSwapsTopAndBottom()
    {
        var document = OneRect();
        var result = Run(document, d => PieceOperations.MirrorHorizontal(d, d.Pieces[0].Id));
        Assert.Equal(Pts((0, 80), (120, 80), (120, 0), (0, 0)), result.Piece!.Points);
        Assert.Equal("“Ön beden” yatay eksende (alt-üst) aynalandı.", result.Summary);
    }

    [Fact]
    public void MirrorKeepsTheBoundingBoxExactly()
    {
        var triangle = new PatternPiece { Name = "Üçgen", Points = [new(10, 10), new(110, 10), new(60, 70)] };
        var before = Geometry.Bounds(triangle.Points);
        foreach (var op in new Func<ProjectDocument, PieceOperationResult>[]
                 { d => PieceOperations.MirrorVertical(d, triangle.Id), d => PieceOperations.MirrorHorizontal(d, triangle.Id) })
        {
            var result = Run(Doc(triangle), op);
            Assert.Equal(before, Geometry.Bounds(result.Piece!.Points));
        }
    }

    [Fact]
    public void MirrorChangesTheOrientationOfAClosedContour()
    {
        var document = OneRect();
        var result = Run(document, d => PieceOperations.MirrorVertical(d, d.Pieces[0].Id));
        Assert.Equal(-Geometry.SignedArea(document.Pieces[0].Points), Geometry.SignedArea(result.Piece!.Points));
    }

    [Fact]
    public void MirroringTwiceReturnsTheOriginalPoints()
    {
        var document = Doc(Rect("A", 3, 7, 121, 81));
        var id = document.Pieces[0].Id;
        var once = Run(document, d => PieceOperations.MirrorVertical(d, id));
        var twice = Run(once.Document!, d => PieceOperations.MirrorVertical(d, id));
        Assert.Equal(document.Pieces[0].Points, twice.Piece!.Points);
        var h1 = Run(document, d => PieceOperations.MirrorHorizontal(d, id));
        var h2 = Run(h1.Document!, d => PieceOperations.MirrorHorizontal(d, id));
        Assert.Equal(document.Pieces[0].Points, h2.Piece!.Points);
    }

    [Fact]
    public void MirrorKeepsAnOpenPieceOpen()
    {
        var path = new PatternPiece { Name = "Yol", Closed = false, Points = [new(0, 0), new(10, 10), new(20, 0)] };
        var result = Run(Doc(path), d => PieceOperations.MirrorVertical(d, path.Id));
        Assert.False(result.Piece!.Closed);
        Assert.Equal(Pts((20, 0), (10, 10), (0, 0)), result.Piece.Points);
    }

    [Fact]
    public void PieceWithCurvesIsRefused()
    {
        var curved = PieceOperationCurveTests.CurvedPiece("Eğri");
        AssertRefused(PieceOperations.MirrorVertical(Doc(curved), curved.Id), PieceOperationError.Unsupported,
            "“Eğri” yay veya eğri içeriyor; aynalama eğrilerin kontrol noktalarını bozacağı için uygulanmadı. Eğri içeren parçalarda yalnızca taşıma ve çoğaltma desteklenir.");
        AssertRefused(PieceOperations.MirrorHorizontal(Doc(curved), curved.Id), PieceOperationError.Unsupported,
            "“Eğri” yay veya eğri içeriyor; aynalama eğrilerin kontrol noktalarını bozacağı için uygulanmadı. Eğri içeren parçalarda yalnızca taşıma ve çoğaltma desteklenir.");
    }

    [Fact]
    public void PieceOnALockedLayerIsRefused()
    {
        var layer = new Layer { Name = "Kilitli", Locked = true };
        var document = WithLayers(OneRect(), layer);
        document.Pieces[0].LayerId = layer.Id;
        AssertRefused(PieceOperations.MirrorVertical(document, document.Pieces[0].Id), PieceOperationError.LayerLocked,
            "Parça kilitli bir katmanda. Ayrıntılı görünümde katmanın kilidini açın.");
    }

    [Fact]
    public void UnknownPieceIsRefused()
    {
        AssertRefused(PieceOperations.MirrorHorizontal(OneRect(), Guid.NewGuid()), PieceOperationError.PieceNotFound,
            "Parça bulunamadı; silinmiş olabilir. Parçayı yeniden seçip komutu tekrar verin.");
    }

    [Fact]
    public void MirrorUndoesInOneStep()
    {
        var document = Doc(Rect("A", 0, 0, 10, 20), Rect("B", 50, 0, 10, 10));
        AssertUndoRoundTrip(document, d => PieceOperations.MirrorVertical(d, d.Pieces[0].Id));
        AssertUndoRoundTrip(document, d => PieceOperations.MirrorHorizontal(d, d.Pieces[1].Id));
    }
}
