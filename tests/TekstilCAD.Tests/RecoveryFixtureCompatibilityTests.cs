using System.Text;
using System.Text.Json;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>Hand-authored envelope fixtures and independent JSON interpretation protect persisted semantics.</summary>
public sealed class RecoveryFixtureCompatibilityTests
{
    private const string PieceId = "11111111-1111-1111-1111-111111111111";
    private const string OrphanId = "22222222-2222-2222-2222-222222222222";
    private static string DirectoryForTest()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        return Path.Combine(root!.FullName, "work", "recovery-fixture-compatibility", Guid.NewGuid().ToString("N"));
    }

    // These literals are not produced by the serializer under test. QQAA2A== is
    // UTF-16LE code units U+0041 and an unpaired U+D800, not replacement text.
    private static string Fixture(int version)
    {
        const string template = """
        {"Version":VERSION,"Project":[2,{"Name":"Factory","Pieces":[{"Id":"PIECE","Name":"Front","Points":[{"X":0,"Y":0},{"X":10,"Y":0},{"X":0,"Y":10}],"Closed":true,"Metadata":{"Role":"Kesim","Size":"M","Material":"cotton","Quantity":2}}],"GradingRules":[{"SourcePieceId":"PIECE","SourceClosed":true,"SourcePoints":[{"X":0,"Y":0},{"X":10,"Y":0},{"X":0,"Y":10}],"Deltas":[{"X":1,"Y":2},{"X":0,"Y":0},{"X":-1,"Y":3}]}]}],"Drafts":[{"PieceId":"ORPHAN","Name":"QQAA2A==","X":"LQA=","Y":"","BaseName":"QgA=","BaseX":3,"BaseY":4}]PENDING}
        """;
        return template.Replace("VERSION", version.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("PIECE", PieceId).Replace("ORPHAN", OrphanId)
            .Replace("PENDING", version == 2 ? ",\"PendingDrawing\":{\"Tool\":\"Çizgi\",\"Points\":[{\"X\":1.25,\"Y\":-2.5}]}" : "");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void HandAuthoredOldEnvelopeRestoresMetadataRulesAndOrphanDraft(int version)
    {
        var directory = DirectoryForTest(); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"session-{Guid.NewGuid():N}.tcr");
        var original = Encoding.UTF8.GetBytes(Fixture(version)); File.WriteAllBytes(path, original);
        using var reader = new RecoveryStore(directory);
        var snapshot = reader.LoadCandidate(path);
        var piece = Assert.Single(snapshot.Document.Pieces);
        Assert.Equal(Guid.Parse(PieceId), piece.Id);
        Assert.Equal("cotton", piece.Metadata!.Material);
        Assert.Equal(2, piece.Metadata.Quantity);
        Assert.Equal(new Point2(-1, 3), Assert.Single(snapshot.Document.GradingRules).Deltas[2]);
        var orphan = Assert.Single(snapshot.Drafts);
        Assert.Equal(Guid.Parse(OrphanId), orphan.PieceId);
        Assert.Equal("A\ud800", orphan.Name);
        Assert.Equal("-", orphan.X);
        Assert.DoesNotContain(snapshot.Document.Pieces, p => p.Id == orphan.PieceId);
        if (version == 1) Assert.Null(snapshot.PendingDrawing);
        else Assert.Equal(new Point2(1.25, -2.5), Assert.Single(snapshot.PendingDrawing!.Points));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void NewV2EnvelopeRetainsRawDraftUnitsAndVersionedProjectForOldStyleJsonReaders()
    {
        var directory = DirectoryForTest();
        var piece = new PatternPiece { Id = Guid.Parse(PieceId), Name = "Front", Points = [new(0, 0), new(10, 0), new(0, 10)], Metadata = new() { Role = "Kesim", Quantity = 2 } };
        var document = new ProjectDocument { Name = "Factory", Pieces = [piece], GradingRules = [new() { SourcePieceId = piece.Id, SourceClosed = true, SourcePoints = [.. piece.Points], Deltas = [new(1, 2), new(0, 0), new(-1, 3)] }] };
        var draft = new RecoveryPropertyDraft(Guid.Parse(OrphanId), "A\ud800", "-", "", "B", 3, 4);
        string path;
        using (var writer = new RecoveryStore(directory)) { writer.Save(document, [draft], new RecoveryPendingDrawing("Çizgi", [new(1.25, -2.5)])); path = writer.DraftRecoveryPath; }
        using var json = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = json.RootElement;
        Assert.Equal(2, root.GetProperty("Version").GetInt32());
        Assert.Equal(2, root.GetProperty("Project")[0].GetInt32());
        Assert.Equal("Kesim", root.GetProperty("Project")[1].GetProperty("Pieces")[0].GetProperty("Metadata").GetProperty("Role").GetString());
        Assert.Equal(-1, root.GetProperty("Project")[1].GetProperty("GradingRules")[0].GetProperty("Deltas")[2].GetProperty("X").GetDouble());
        Assert.Equal(new byte[] { 0x41, 0x00, 0x00, 0xd8 }, Convert.FromBase64String(root.GetProperty("Drafts")[0].GetProperty("Name").GetString()!));
        Assert.Equal(Guid.Parse(OrphanId), root.GetProperty("Drafts")[0].GetProperty("PieceId").GetGuid());
        Assert.Equal(-2.5, root.GetProperty("PendingDrawing").GetProperty("Points")[0].GetProperty("Y").GetDouble());
    }
}
