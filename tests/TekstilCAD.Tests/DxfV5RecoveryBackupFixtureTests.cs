using System.Text;
using System.Text.Json;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfV5RecoveryBackupFixtureTests
{
    private const string PieceId = "11111111-1111-1111-1111-111111111111";
    private const string HistoricalId = "22222222-2222-2222-2222-222222222222";
    // Hand-authored, so compatibility does not depend on the current writer.
    private const string Project = """
        [5,{"Name":"Factory","Pieces":[{"Id":"11111111-1111-1111-1111-111111111111","Name":"Front","Points":[{"X":0,"Y":0},{"X":10,"Y":0},{"X":0,"Y":10}],"Closed":true}],"GradingRules":[],"ImportProvenance":{"SourceFileName":"kaynak.dxf","SourceSha256":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","SourceByteLength":53,"MillimetersPerUnit":25.4,"Warnings":["Kontrol edin","İkinci uyarı"],"PieceIds":["11111111-1111-1111-1111-111111111111","22222222-2222-2222-2222-222222222222"]}}]
        """;

    private static string DirectoryForTest()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        var dir = Path.Combine(root!.FullName, "work", "dxf-v5-recovery-backup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void AssertProvenance(ProjectDocument document)
    {
        var provenance = Assert.IsType<DxfImportProvenance>(document.ImportProvenance);
        Assert.Equal("kaynak.dxf", provenance.SourceFileName);
        Assert.Equal(new string('A', 64), provenance.SourceSha256);
        Assert.Equal(53, provenance.SourceByteLength);
        Assert.Equal(25.4, provenance.MillimetersPerUnit);
        Assert.Equal(new[] { "Kontrol edin", "İkinci uyarı" }, provenance.Warnings);
        Assert.Equal(new[] { Guid.Parse(PieceId), Guid.Parse(HistoricalId) }, provenance.PieceIds);
        Assert.DoesNotContain(document.Pieces, p => p.Id == Guid.Parse(HistoricalId));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void HandAuthoredEnvelopeLoadsV5AndWriterPreservesIt(int envelopeVersion)
    {
        var dir = DirectoryForTest();
        var pending = envelopeVersion == 2 ? ",\"PendingDrawing\":{\"Tool\":\"Çizgi\",\"Points\":[{\"X\":1,\"Y\":2}]}" : "";
        var envelope = $"{{\"Version\":{envelopeVersion},\"Project\":{Project},\"Drafts\":[]{pending}}}";
        var path = Path.Combine(dir, $"session-{Guid.NewGuid():N}.tcr");
        var original = Encoding.UTF8.GetBytes(envelope);
        File.WriteAllBytes(path, original);
        using var reader = new RecoveryStore(dir);
        var snapshot = reader.LoadCandidate(path);
        AssertProvenance(snapshot.Document);
        Assert.Equal(original, File.ReadAllBytes(path));
        if (envelopeVersion == 2) Assert.Equal(new Point2(1, 2), Assert.Single(snapshot.PendingDrawing!.Points));
        else Assert.Null(snapshot.PendingDrawing);
        string writtenPath;
        using (var writer = new RecoveryStore(dir))
        {
            writer.Save(snapshot.Document, snapshot.Drafts, snapshot.PendingDrawing);
            writtenPath = writer.DraftRecoveryPath;
        }
        using var output = JsonDocument.Parse(File.ReadAllBytes(writtenPath));
        Assert.Equal(5, output.RootElement.GetProperty("Project")[0].GetInt32());
        Assert.Equal("kaynak.dxf", output.RootElement.GetProperty("Project")[1].GetProperty("ImportProvenance").GetProperty("SourceFileName").GetString());
        AssertProvenance(reader.LoadCandidate(writtenPath).Document);
    }

    [Fact]
    public void IncompleteV5HistoryIsReportedWithoutHidingOtherRecoveryOrChangingBytes()
    {
        var dir = DirectoryForTest();
        var bad = Path.Combine(dir, $"session-{Guid.NewGuid():N}.tcr");
        var malformed = Project.Replace("\"SourceByteLength\":53,", "", StringComparison.Ordinal);
        Assert.NotEqual(Project, malformed);
        var bytes = Encoding.UTF8.GetBytes($"{{\"Version\":1,\"Project\":{malformed},\"Drafts\":[]}}");
        File.WriteAllBytes(bad, bytes);
        string good;
        using (var writer = new RecoveryStore(dir))
        {
            writer.Save(new ProjectDocument { Name = "Sağlam" }, Array.Empty<RecoveryPropertyDraft>());
            good = writer.DraftRecoveryPath;
        }
        var scan = RecoveryStore.ScanDirectory(dir);
        Assert.Contains(scan.Candidates, c => c.Path == bad && !c.IsRecoverable);
        Assert.Contains(scan.Candidates, c => c.Path == good && c.IsRecoverable);
        Assert.Equal(bytes, File.ReadAllBytes(bad));
    }

    [Fact]
    public void BackupRestoresIndependentV5ProjectBytesAndHistory()
    {
        var dir = DirectoryForTest();
        var source = Path.Combine(dir, "source"); Directory.CreateDirectory(source);
        var bytes = Encoding.UTF8.GetBytes(Project);
        File.WriteAllBytes(Path.Combine(source, "fixture.tcad"), bytes);
        var archive = Path.Combine(dir, "backup.zip");
        ProjectBackup.Create(source, archive);
        var destination = Path.Combine(dir, "restored");
        ProjectBackup.Restore(archive, destination);
        var restored = Path.Combine(destination, "fixture.tcad");
        Assert.Equal(bytes, File.ReadAllBytes(restored));
        AssertProvenance(ProjectStorage.Load(restored));
    }
}
