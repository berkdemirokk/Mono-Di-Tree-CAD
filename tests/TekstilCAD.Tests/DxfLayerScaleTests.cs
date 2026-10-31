using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfLayerScaleTests
{
    private static PatternPiece Piece(string name) => new()
    {
        Name = name, Closed = false, Points = [new(0, 0), new(1, 1)]
    };

    [Fact]
    public void TenThousandIdenticalNamesAreUniqueStableAndDoNotMutateSources()
    {
        var document = new ProjectDocument
        {
            Pieces = Enumerable.Range(0, ProjectStorage.MaximumPieces).Select(_ => Piece("Ön beden")).ToList()
        };
        var ids = document.Pieces.Select(p => p.Id).ToArray();

        var first = DxfFormat.PlanLayers(document);
        var second = DxfFormat.PlanLayers(document);

        Assert.Equal(ProjectStorage.MaximumPieces, first.Count);
        Assert.Equal(first.Count, first.Select(x => x.LayerName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(first.Select(x => (x.PieceId, x.LayerName)), second.Select(x => (x.PieceId, x.LayerName)));
        Assert.Equal("Ön beden", first[0].LayerName);
        Assert.Equal("Ön beden_10000", first[^1].LayerName);
        Assert.Equal(ids, document.Pieces.Select(p => p.Id));
        Assert.All(document.Pieces, p => Assert.Equal("Ön beden", p.Name));
    }

    [Fact]
    public void ExistingSuffixesAndUnicodeTruncationRemainUniqueAndWellFormed()
    {
        var prefix = new string('a', 253) + "😀";
        var document = new ProjectDocument
        {
            Pieces = [Piece("parça"), Piece("PARÇA_2"), Piece("parça"), Piece("Parça_3"),
                Piece(prefix), Piece(prefix), Piece(new string('a', 254) + "😀")]
        };

        var first = DxfFormat.PlanLayers(document);
        Assert.Equal(new[] { "parça", "PARÇA_2", "parça_3", "Parça_3_2" }, first.Take(4).Select(x => x.LayerName));
        Assert.Equal(first.Count, first.Select(x => x.LayerName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var strictUtf8 = new UTF8Encoding(false, true);
        Assert.All(first, item =>
        {
            Assert.InRange(item.LayerName.Length, 1, 255);
            Assert.Equal(item.LayerName, item.LayerName.Trim());
            Assert.Equal(item.LayerName, strictUtf8.GetString(strictUtf8.GetBytes(item.LayerName)));
        });
        Assert.Equal(first.Select(x => x.LayerName), DxfFormat.PlanLayers(document).Select(x => x.LayerName));
    }
}
