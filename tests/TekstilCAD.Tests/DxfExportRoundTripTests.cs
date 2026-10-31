using System.Security.Cryptography;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfExportRoundTripTests
{
    [Fact]
    public void ExportAndImportPreserveMultipleContoursCoordinatesClosureAndMillimeterHeader()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var folder = Path.Combine(root!.FullName, "work", "dxf-roundtrip-tests");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".dxf");
        var document = new ProjectDocument { Name = "Test", Pieces =
        [
            new PatternPiece { Name = "Kapali", Closed = true, Points = [new(0.000001, -0.000002), new(10.125, -0.000002), new(10.125, 20.5), new(0.000001, 20.5)] },
            new PatternPiece { Name = "Acik", Closed = false, Points = [new(99999900.123456, -99999900.654321), new(99999901.123456, -99999899.654321)] }
        ] };
        var before = document.Clone();
        try
        {
            DxfFormat.Export(document, path);
            Assert.True(DocumentSession.ContentEquals(before, document));
            var bytes = File.ReadAllBytes(path);
            var hash = SHA256.HashData(bytes);
            var result = DxfFormat.Import(path, 1);
            Assert.True(result.Units.IsReliable);
            Assert.Equal(4, result.Units.Code);
            Assert.Equal(1, result.Units.MillimetersPerUnit);
            Assert.Equal(2, result.Project.Pieces.Count);
            for (int i = 0; i < document.Pieces.Count; i++)
            {
                var expected = document.Pieces[i]; var actual = result.Project.Pieces[i];
                Assert.Equal(expected.Closed, actual.Closed);
                Assert.Equal(expected.Name, actual.Name);
                Assert.Equal(expected.Points, actual.Points);
            }
            Assert.True(DocumentSession.ContentEquals(before, document));
            Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
