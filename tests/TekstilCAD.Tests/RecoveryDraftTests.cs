using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class RecoveryDraftTests
{
    private static string TestDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        return Path.Combine(root.FullName, "work", "recovery-draft-tests", Guid.NewGuid().ToString("N"));
    }

    private static ProjectDocument Document() => new()
    {
        Name = "Taslaklı çalışma",
        Pieces = [new PatternPiece { Name = "Ön", Points = [new(0,0), new(10,0), new(0,10)] }]
    };

    [Fact]
    public void DraftOnlySnapshotPreservesInvalidTextAndDeletedPieceWithoutApplyingThem()
    {
        var directory = TestDirectory();
        var document = Document();
        var drafts = new[]
        {
            new RecoveryPropertyDraft(document.Pieces[0].Id, "İsim\ud800", "1,2,3", "", "Ön", 0, 0),
            new RecoveryPropertyDraft(Guid.NewGuid(), "Silinmiş", "-", "abc", "Eski", 3, 4)
        };
        string path;
        using (var crashed = new RecoveryStore(directory))
        {
            crashed.Save(document, drafts);
            path = crashed.DraftRecoveryPath;
        }
        using var reader = new RecoveryStore(directory);
        var candidate = Assert.Single(reader.Scan().Candidates);
        Assert.Equal(path, candidate.Path);
        var recovered = reader.LoadCandidate(path);
        Assert.Equal("Ön", Assert.Single(recovered.Document.Pieces).Name);
        Assert.Equal(drafts, recovered.Drafts);
        Assert.Equal("İsim\ud800", recovered.Drafts[0].Name);
        Assert.DoesNotContain(recovered.Document.Pieces, p => p.Id == recovered.Drafts[1].PieceId);
    }

    [Fact]
    public void OldRecoveryRemainsReadableWithNoDrafts()
    {
        var directory = TestDirectory();
        string path;
        using (var previous = new RecoveryStore(directory)) { previous.Save(Document()); path = previous.RecoveryPath; }
        using var current = new RecoveryStore(directory);
        var recovered = current.LoadCandidate(path);
        Assert.Equal("Taslaklı çalışma", recovered.Document.Name);
        Assert.Empty(recovered.Drafts);
        Assert.True(Assert.Single(current.Scan().Candidates).IsRecoverable);
    }

    [Fact]
    public void OtherLiveSessionIsExcludedAndCannotBeLoadedUntilLeaseReleased()
    {
        var directory = TestDirectory();
        var document = Document();
        var draft = new RecoveryPropertyDraft(document.Pieces[0].Id, "A", "?", "0", "Ön", 0, 0);
        using var observer = new RecoveryStore(directory);
        var writer = new RecoveryStore(directory);
        try
        {
            writer.Save(document, [draft]);
            Assert.Empty(observer.Scan().Candidates);
            Assert.Throws<IOException>(() => observer.LoadCandidate(writer.DraftRecoveryPath));
            writer.Dispose();
            Assert.Equal(draft, Assert.Single(observer.LoadCandidate(Assert.Single(observer.Scan().Candidates).Path).Drafts));
        }
        finally { writer.Dispose(); }
    }

    [Fact]
    public void CorruptEnvelopeDoesNotHideOtherCandidate()
    {
        var directory = TestDirectory();
        string good;
        using (var writer = new RecoveryStore(directory)) { writer.Save(Document(), []); good = writer.DraftRecoveryPath; }
        var bad = Path.Combine(directory, $"session-{Guid.NewGuid():N}.tcr");
        File.WriteAllText(bad, "{broken");
        using var observer = new RecoveryStore(directory);
        var scan = observer.Scan();
        Assert.Equal(2, scan.Candidates.Count);
        Assert.True(Assert.Single(scan.Candidates.Where(c => c.Path == good)).IsRecoverable);
        Assert.False(Assert.Single(scan.Candidates.Where(c => c.Path == bad)).IsRecoverable);
        Assert.Single(scan.Errors);
        Assert.Equal("Taslaklı çalışma", observer.LoadCandidate(good).Document.Name);
        Assert.True(File.Exists(bad));
    }

    [Fact]
    public void FailedReplacementLeavesLastCompleteSnapshotReadable()
    {
        var directory = TestDirectory();
        var document = Document();
        var first = new RecoveryPropertyDraft(document.Pieces[0].Id, "İlk", "?", "0", "Ön", 0, 0);
        string path;
        using (var writer = new RecoveryStore(directory))
        {
            writer.Save(document, [first]);
            path = writer.DraftRecoveryPath;
            var originalBytes = File.ReadAllBytes(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var error = Record.Exception(() => writer.Save(document, [first with { Name = "İkinci" }]));
                Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString() ?? "Expected a failed replacement.");
            }
            Assert.Equal(originalBytes, File.ReadAllBytes(path));
        }
        using var reader = new RecoveryStore(directory);
        Assert.Equal(first, Assert.Single(reader.LoadCandidate(path).Drafts));
    }

    [Fact]
    public void ArchiveRejectsModifiedSnapshotAndPreservesIt()
    {
        var directory = TestDirectory();
        string path;
        using (var writer = new RecoveryStore(directory)) { writer.Save(Document(), []); path = writer.DraftRecoveryPath; }
        using var reader = new RecoveryStore(directory);
        var recovered = reader.LoadCandidate(path);
        File.AppendAllText(path, " ");
        Assert.Throws<IOException>(() => reader.ArchiveRecovered(recovered));
        Assert.True(File.Exists(path));
        var fresh = reader.LoadCandidate(path);
        var archived = reader.ArchiveRecovered(fresh);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(archived));
        Assert.Equal(".tcr", Path.GetExtension(archived));
    }
}
