using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class RecoveryStoreTests
{
    private static string TestDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        return Path.Combine(root.FullName, "work", "recovery-tests", Guid.NewGuid().ToString("N"));
    }
    private static ProjectDocument Document(string name = "Kurtarılan kalıp") => new() { Name = name };
    private static string CandidatePath(string directory) => Path.Combine(directory, $"session-{Guid.NewGuid():N}.tcad");

    [Fact]
    public void LiveSessionsAreExcludedAndDisposedSessionBecomesRecoverable()
    {
        var directory = TestDirectory();
        var first = new RecoveryStore(directory);
        using var second = new RecoveryStore(directory);
        try
        {
            first.Save(Document("Birinci oturum"));
            second.Save(Document("İkinci oturum"));
            Assert.Empty(second.Scan().Candidates);
            Assert.Empty(first.Scan().Candidates);
            Assert.Throws<IOException>(() => second.LoadCandidate(first.RecoveryPath));
            first.Dispose();
            var candidate = Assert.Single(second.Scan().Candidates);
            Assert.True(candidate.IsRecoverable);
            Assert.Equal("Birinci oturum", candidate.ProjectName);
            Assert.Equal("Birinci oturum", second.LoadCandidate(candidate.Path).Document.Name);
        }
        finally { first.Dispose(); }
    }

    [Fact]
    public void CorruptNewestCandidateDoesNotHideOlderValidFile()
    {
        var directory = TestDirectory();
        using var session = new RecoveryStore(directory);
        var good = CandidatePath(directory);
        var bad = CandidatePath(directory);
        ProjectStorage.Save(Document("Eski sağlam kayıt"), good);
        File.WriteAllText(bad, "{ broken json");
        File.SetLastWriteTimeUtc(good, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(bad, DateTime.UtcNow);
        var scan = session.Scan();
        Assert.Equal(2, scan.Candidates.Count);
        Assert.Equal(bad, scan.Candidates[0].Path);
        Assert.False(scan.Candidates[0].IsRecoverable);
        Assert.True(scan.Candidates[1].IsRecoverable);
        Assert.Single(scan.Errors);
        Assert.Equal("Eski sağlam kayıt", session.LoadCandidate(good).Document.Name);
        Assert.True(File.Exists(bad));
    }

    [Fact]
    public void MissingDirectoryScanDoesNotCreateIt()
    {
        var directory = TestDirectory();
        var scan = RecoveryStore.ScanDirectory(directory);
        Assert.Empty(scan.Candidates);
        Assert.Empty(scan.Errors);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void ClearOnlyRemovesOwnSnapshotAndDisposePreservesOtherSnapshots()
    {
        var directory = TestDirectory();
        using var first = new RecoveryStore(directory);
        var second = new RecoveryStore(directory);
        first.Save(Document("A")); second.Save(Document("B"));
        Assert.True(first.ClearOwn());
        Assert.False(File.Exists(first.RecoveryPath));
        Assert.True(File.Exists(second.RecoveryPath));
        second.Dispose();
        Assert.True(File.Exists(second.RecoveryPath));
        Assert.False(first.ClearOwn());
        Assert.Throws<ObjectDisposedException>(() => second.ClearOwn());
    }

    [Fact]
    public void ArchiveRequiresUnchangedSourceAndPreservesChangedFile()
    {
        var directory = TestDirectory();
        using var session = new RecoveryStore(directory);
        string path;
        using(var original=new RecoveryStore(directory)) { original.Save(Document("İlk"));path=original.RecoveryPath; }
        var recovered = session.LoadCandidate(path);
        ProjectStorage.Save(Document("Daha yeni"), path);
        Assert.Throws<IOException>(() => session.ArchiveRecovered(recovered));
        Assert.Equal("Daha yeni", ProjectStorage.Load(path).Name);
        var fresh = session.LoadCandidate(path);
        var archived = session.ArchiveRecovered(fresh);
        Assert.False(File.Exists(path));
        Assert.Equal("Daha yeni", ProjectStorage.Load(archived).Name);
        Assert.Empty(session.Scan().Candidates);
    }

    [Fact]
    public void ArchiveRejectsLeaseAcquiredAfterCandidateWasLoaded()
    {
        var directory = TestDirectory();
        using var session = new RecoveryStore(directory);
        var path = CandidatePath(directory);
        ProjectStorage.Save(Document(), path);
        var recovered = session.LoadCandidate(path);
        using var otherLease = new FileStream(Path.ChangeExtension(path, ".lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<IOException>(() => session.ArchiveRecovered(recovered));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void CandidateOutsideManagedDirectoryAndOwnCandidateAreRejected()
    {
        var directory = TestDirectory();
        using var session = new RecoveryStore(directory);
        session.Save(Document());
        Assert.Throws<InvalidOperationException>(() => session.LoadCandidate(session.RecoveryPath));
        Assert.Throws<InvalidOperationException>(() => session.LoadCandidate(Path.Combine(directory, "..", $"session-{Guid.NewGuid():N}.tcad")));
        Assert.Throws<InvalidOperationException>(() => session.LoadCandidate(Path.Combine(directory, "project.tcad")));
    }

    [Fact]
    public void ProtectedRootIsRejectedBeforeAnyRecoveryWrite()
    {
        Assert.Throws<InvalidOperationException>(() => new RecoveryStore(@"C:\WearCadfe\recovery-must-not-be-created"));
        var scan = RecoveryStore.ScanDirectory(@"C:\WearCadfe\recovery-must-not-be-created");
        Assert.Empty(scan.Candidates);
        Assert.Single(scan.Errors);
    }

    [Fact]
    public void InvalidSaveKeepsLastGoodSnapshot()
    {
        using var session = new RecoveryStore(TestDirectory());
        session.Save(Document("Sağlam"));
        var invalid = Document(); invalid.Name = null!;
        Assert.Throws<InvalidDataException>(() => session.Save(invalid));
        Assert.Equal("Sağlam", ProjectStorage.Load(session.RecoveryPath).Name);
    }

    [Fact]
    public void TrailingDirectorySeparatorDoesNotRejectValidCandidate()
    {
        var directory = TestDirectory();
        using var session = new RecoveryStore(directory + Path.DirectorySeparatorChar);
        var path = CandidatePath(directory);
        ProjectStorage.Save(Document(), path);
        Assert.True(Assert.Single(session.Scan().Candidates).IsRecoverable);
        Assert.Equal(path, session.LoadCandidate(path).SourcePath);
    }

    [Fact]
    public void DirectoryEnumerationFailureIsReturnedAsError()
    {
        var path = TestDirectory();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "This is a file, not a recovery directory.");
        var scan = RecoveryStore.ScanDirectory(path);
        Assert.Empty(scan.Candidates);
        Assert.Single(scan.Errors);
    }
}
