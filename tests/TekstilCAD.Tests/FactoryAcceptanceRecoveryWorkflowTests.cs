using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class FactoryAcceptanceRecoveryWorkflowTests
{
    [Fact]
    public void RecoveryConflictPreservesBothOperatorsWorkAndUnappliedDraft()
    {
        // Synthetic production-like data; no factory files are used.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root!.FullName, "work", "factory-recovery-workflow", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "siparis.tcad");
        var piece = new PatternPiece
        {
            Name = "Ön", Points = [new(0, 0), new(100, 0), new(0, 100)],
            Metadata = new() { Role = "Kesim", Size = "M", Material = "Pamuk", Quantity = 2 }
        };
        var original = new ProjectDocument { Name = "Örnek sipariş", Pieces = [piece] };
        ProjectStorage.Save(original, projectPath);
        var versionBeforeOtherOperator = ProjectStorage.CaptureVersion(projectPath);

        var unsaved = original.Clone();
        unsaved.Pieces[0].Metadata!.Quantity = 5;
        var draft = new RecoveryPropertyDraft(piece.Id, "Ön taslak", "?", "0", "Ön", 0, 0);
        string recoveryPath;
        using (var crashed = new RecoveryStore(Path.Combine(directory, "recovery")))
        {
            crashed.Save(unsaved, [draft]);
            recoveryPath = crashed.DraftRecoveryPath;
        }
        var otherOperator = original.Clone();
        otherOperator.Pieces[0].Metadata!.Size = "L";
        ProjectStorage.Save(otherOperator, projectPath);
        var otherBytes = File.ReadAllBytes(projectPath);
        var recoveryBytes = File.ReadAllBytes(recoveryPath);

        using var reader = new RecoveryStore(Path.Combine(directory, "recovery"));
        var recovered = reader.LoadCandidate(recoveryPath);
        Assert.Equal(draft, Assert.Single(recovered.Drafts));
        Assert.Throws<ProjectConflictException>(() => ProjectStorage.SaveIfUnchanged(recovered.Document, projectPath, versionBeforeOtherOperator));
        Assert.Equal(otherBytes, File.ReadAllBytes(projectPath));
        Assert.Equal(recoveryBytes, File.ReadAllBytes(recoveryPath));
        Assert.Equal("L", ProjectStorage.Load(projectPath).Pieces[0].Metadata!.Size);

        var copyPath = Path.Combine(directory, "kurtarilan.tcad");
        ProjectStorage.SaveIfUnchanged(recovered.Document, copyPath, ProjectStorage.CaptureVersion(copyPath));
        Assert.True(DocumentSession.ContentEquals(unsaved, ProjectStorage.Load(copyPath)));
        var archivedPath = reader.ArchiveRecovered(recovered);
        Assert.False(File.Exists(recoveryPath));
        Assert.Equal(recoveryBytes, File.ReadAllBytes(archivedPath));
        Assert.Equal(otherBytes, File.ReadAllBytes(projectPath));
    }
}