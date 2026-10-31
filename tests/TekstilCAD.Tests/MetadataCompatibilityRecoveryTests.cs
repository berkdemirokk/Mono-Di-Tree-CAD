using System.Text.Json;
using System.Text.Json.Serialization;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class MetadataCompatibilityRecoveryTests
{
    // Mirrors the persisted properties understood by the 0.4 project reader.
    private sealed class OldDocument
    {
        public string? Name { get; set; }
        public List<OldPiece>? Pieces { get; set; }
        public List<GradingRuleSet>? GradingRules { get; set; }
    }
    private sealed class OldPiece
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public List<Point2>? Points { get; set; }
        public bool Closed { get; set; }
    }

    private static string TestDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root!.FullName, "work", "metadata-compat-recovery", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static ProjectDocument Document()
    {
        var piece = new PatternPiece { Name = "Ön", Points = [new(0, 0), new(100, 0), new(0, 100)] };
        return new ProjectDocument { Name = "Üretim", Pieces = [piece] };
    }

    [Fact]
    public void LegacyObjectsAndVersionOneOmitMetadataAndVersionTwoCannotBeReadAsOldProject()
    {
        var dir = TestDirectory(); var path = Path.Combine(dir, "proje.tcad"); var doc = Document();
        var oldOptions = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        ProjectStorage.Save(doc, path);
        var plain = File.ReadAllText(path);
        using (var json = JsonDocument.Parse(plain)) Assert.Equal(JsonValueKind.Object, json.RootElement.ValueKind);
        Assert.NotNull(JsonSerializer.Deserialize<OldDocument>(plain, oldOptions));
        Assert.Null(ProjectStorage.Load(path).Pieces[0].Metadata);

        var piece = doc.Pieces[0];
        doc.GradingRules.Add(new() { SourcePieceId = piece.Id, SourceClosed = true, SourcePoints = [.. piece.Points], Deltas = [new(1, 0), new(1, 0), new(1, 0)] });
        ProjectStorage.Save(doc, path);
        using (var json = JsonDocument.Parse(File.ReadAllText(path)))
        {
            Assert.Equal(1, json.RootElement[0].GetInt32());
            Assert.NotNull(json.RootElement[1].Deserialize<OldDocument>(oldOptions));
            Assert.False(json.RootElement[1].GetProperty("Pieces")[0].TryGetProperty("Metadata", out _));
        }
        Assert.Null(ProjectStorage.Load(path).Pieces[0].Metadata);

        piece.Metadata = new() { Role = "Kesim", Size = "M", Material = "Pamuk", Quantity = 3 };
        ProjectStorage.Save(doc, path);
        using var version2 = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(2, version2.RootElement[0].GetInt32());
        Assert.Throws<JsonException>(() => version2.RootElement[1].Deserialize<OldDocument>(oldOptions));
    }

    [Fact]
    public void MetadataAndRulesSurviveRecoveryArchiveAndRestoreWithUndo()
    {
        var dir = TestDirectory(); var doc = Document(); var piece = doc.Pieces[0];
        piece.Metadata = new() { Role = "Yardımcı", Size = "XL", Material = "Örme", Quantity = 4 };
        doc.GradingRules.Add(new() { SourcePieceId = piece.Id, SourceClosed = true, SourcePoints = [.. piece.Points], Deltas = [new(1, 2), new(0, 0), new(-1, 3)] });
        string source;
        using (var previous = new RecoveryStore(dir)) { previous.Save(doc); source = previous.RecoveryPath; }
        using var current = new RecoveryStore(dir);
        var candidate = Assert.Single(current.Scan().Candidates);
        Assert.Equal(source, candidate.Path);
        var recovered = current.LoadCandidate(candidate.Path);
        var session = new DocumentSession(recovered.Document);
        session.Execute(d => d.Pieces[0].Metadata!.Quantity = 8, ProjectStorage.Validate);
        Assert.True(session.IsDirty);
        Assert.True(session.Undo());
        Assert.True(DocumentSession.ContentEquals(doc, session.Document));
        Assert.True(session.Redo());
        Assert.Equal(8, session.Document.Pieces[0].Metadata!.Quantity);
        Assert.True(session.Undo());
        var dest = Path.Combine(dir, "restored.tcad");
        ProjectStorage.SaveIfUnchanged(session.Document, dest, ProjectStorage.CaptureVersion(dest));
        Assert.True(DocumentSession.ContentEquals(doc, ProjectStorage.Load(dest)));
        var archived = current.ArchiveRecovered(recovered);
        Assert.True(DocumentSession.ContentEquals(doc, ProjectStorage.Load(archived)));
        Assert.False(File.Exists(source));
    }
}
