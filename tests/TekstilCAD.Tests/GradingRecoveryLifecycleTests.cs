using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class GradingRecoveryLifecycleTests
{
    [Fact]
    public void RecoveryPreservesGradingRulesThroughScanSaveAndArchive()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root!.FullName, "work", "grading-recovery-tests", Guid.NewGuid().ToString("N"));
        var piece = new PatternPiece { Points = [new(0, 0), new(10, 0), new(0, 10)] };
        var original = new ProjectDocument { Name = "Beden kuralları", Pieces = [piece], GradingRules = [new()
        {
            SourcePieceId = piece.Id, SourceClosed = piece.Closed, SourcePoints = [.. piece.Points],
            Deltas = [new(1, 2), new(0, 0), new(-1, 3)]
        }] };
        string source;
        using (var crashed = new RecoveryStore(directory))
        {
            crashed.Save(original);
            source = crashed.RecoveryPath;
        }
        using var current = new RecoveryStore(directory);
        var candidate = Assert.Single(current.Scan().Candidates);
        Assert.Equal(source, candidate.Path);
        Assert.True(candidate.IsRecoverable);
        var recovered = current.LoadCandidate(candidate.Path);
        Assert.True(recovered.CanArchive);
        Assert.True(DocumentSession.ContentEquals(original, recovered.Document));
        var savedPath = Path.Combine(directory, "saved.tcad");
        ProjectStorage.SaveIfUnchanged(recovered.Document, savedPath, ProjectStorage.CaptureVersion(savedPath));
        Assert.True(DocumentSession.ContentEquals(original, ProjectStorage.Load(savedPath)));
        var archive = current.ArchiveRecovered(recovered);
        Assert.False(File.Exists(source));
        Assert.True(DocumentSession.ContentEquals(original, ProjectStorage.Load(archive)));
        Assert.Empty(current.Scan().Candidates);
    }
}
