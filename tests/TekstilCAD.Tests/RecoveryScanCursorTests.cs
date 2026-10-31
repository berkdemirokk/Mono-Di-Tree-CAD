using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class RecoveryScanCursorTests
{
    private static string TestDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        return Path.Combine(root.FullName, "work", "recovery-cursor-tests", Guid.NewGuid().ToString("N"));
    }

    private static string CreateRecord(string directory, string name, bool draft)
    {
        using var writer = new RecoveryStore(directory);
        if (draft) { writer.Save(new ProjectDocument { Name = name }, Array.Empty<RecoveryPropertyDraft>()); return writer.DraftRecoveryPath; }
        writer.Save(new ProjectDocument { Name = name });
        return writer.RecoveryPath;
    }

    [Fact]
    public void PagesContinueAcrossBothFormatsAndIsolateBrokenRecords()
    {
        var directory = TestDirectory();
        var oldPath = CreateRecord(directory, "Eski", false);
        var newPath = CreateRecord(directory, "Yeni", true);
        var corruptPath = Path.Combine(directory, $"session-{Guid.NewGuid():N}.tcr");
        File.WriteAllText(corruptPath, "{broken");
        var seen = new List<RecoveryCandidate>();
        var errors = new List<RecoveryScanError>();
        using var cursor = new RecoveryScanCursor(directory, maxCandidatesPerPage: 1);
        for (var i = 0; i < 10; i++)
        {
            var page = cursor.ReadNextPage();
            Assert.InRange(page.Candidates.Count, 0, 1);
            seen.AddRange(page.Candidates); errors.AddRange(page.Errors);
            if (!page.HasMore) break;
            Assert.True(i < 9, "Cursor did not terminate.");
        }
        Assert.Equal(3, seen.Count);
        Assert.Contains(seen, c => c.Path == oldPath && c.IsRecoverable);
        Assert.Contains(seen, c => c.Path == newPath && c.IsRecoverable);
        Assert.Contains(seen, c => c.Path == corruptPath && !c.IsRecoverable);
        Assert.NotEmpty(errors);
        Assert.Equal(3, seen.Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ActiveLeaseIsSkippedWithoutPreventingOtherRecord()
    {
        var directory = TestDirectory();
        using var live = new RecoveryStore(directory);
        live.Save(new ProjectDocument { Name = "Açık" }, Array.Empty<RecoveryPropertyDraft>());
        var other = CreateRecord(directory, "Kapalı", false);
        using var cursor = new RecoveryScanCursor(directory);
        var page = cursor.ReadNextPage();
        Assert.DoesNotContain(page.Candidates, c => c.Path == live.DraftRecoveryPath);
        Assert.Contains(page.Candidates, c => c.Path == other && c.IsRecoverable);
        Assert.True(File.Exists(live.DraftRecoveryPath));
    }

    [Fact]
    public void CancellationAndDisposalLeaveSourceAndLeaseUntouched()
    {
        var directory = TestDirectory();
        var path = CreateRecord(directory, "Kayıt", true);
        var bytes = File.ReadAllBytes(path);
        var cursor = new RecoveryScanCursor(directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => cursor.ReadNextPage(cancellation.Token));
        cursor.Dispose();
        Assert.Throws<ObjectDisposedException>(() => cursor.ReadNextPage());
        Assert.Equal(bytes, File.ReadAllBytes(path));
        using var observer = new RecoveryStore(directory);
        Assert.Equal("Kayıt", observer.LoadCandidate(path).Document.Name);
    }

    [Fact]
    public void ListedCandidateIsReloadedAndChangedSourceCannotBeArchivedAsOriginal()
    {
        var directory = TestDirectory();
        var path = CreateRecord(directory, "İlk", true);
        using var cursor = new RecoveryScanCursor(directory);
        Assert.Contains(cursor.ReadNextPage().Candidates, c => c.Path == path && c.IsRecoverable);
        using var observer = new RecoveryStore(directory);
        var recovered = observer.LoadCandidate(path);
        File.AppendAllText(path, " ");
        Assert.Throws<IOException>(() => observer.ArchiveRecovered(recovered));
        Assert.True(File.Exists(path));
    }
}
