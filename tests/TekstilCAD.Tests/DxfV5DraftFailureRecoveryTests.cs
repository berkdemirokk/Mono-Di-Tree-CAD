using System.Text.Json;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfV5DraftFailureRecoveryTests
{
    [Fact]
    public void DuplicateDraftFailurePreservesCompleteV5Recovery()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root!.FullName, "work", "v5-draft-failure", Guid.NewGuid().ToString("N"));
        var piece = new PatternPiece { Name = "Ön", Points = [new(0, 0), new(10, 0), new(0, 10)] };
        var document = new ProjectDocument { Pieces = [piece], ImportProvenance = new DxfImportProvenance {
            SourceFileName = "kaynak.dxf", SourceSha256 = new string('A', 64), SourceByteLength = 53,
            MillimetersPerUnit = 1, Warnings = ["Önceki uyarı"], PieceIds = [piece.Id] } };
        var draft = new RecoveryPropertyDraft(piece.Id, "Düzenlenmemiş", "?", "0", "Ön", 0, 0);
        var drawing = new RecoveryPendingDrawing("Yay", [new(1, 2), new(3, 4)]);
        string path;
        using (var writer = new RecoveryStore(directory))
        {
            writer.Save(document, [draft], drawing);
            path = writer.DraftRecoveryPath;
            var previousBytes = File.ReadAllBytes(path);
            using (var json = JsonDocument.Parse(previousBytes))
                Assert.Equal(5, json.RootElement.GetProperty("Project")[0].GetInt32());

            document.Name = "Başarısız kayıt";
            Assert.Throws<InvalidDataException>(() => writer.Save(document, [draft, draft], drawing));
            Assert.Equal(previousBytes, File.ReadAllBytes(path));
        }

        using (var reader = new RecoveryStore(directory))
        {
            var restored = reader.LoadCandidate(path);
            Assert.NotEqual(document.Name, restored.Document.Name);
            Assert.Equal("Önceki uyarı", Assert.Single(restored.Document.ImportProvenance!.Warnings));
            Assert.Equal(piece.Id, Assert.Single(restored.Document.ImportProvenance.PieceIds));
            Assert.Equal(draft, Assert.Single(restored.Drafts));
            Assert.Equal(drawing.Tool, restored.PendingDrawing!.Tool);
            Assert.Equal(drawing.Points, restored.PendingDrawing.Points);
        }
    }
}
