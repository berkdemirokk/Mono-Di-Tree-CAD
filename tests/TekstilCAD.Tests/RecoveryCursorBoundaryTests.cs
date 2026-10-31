using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class RecoveryCursorBoundaryTests
{
    private static string NewDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        var path = Path.Combine(root.FullName, "work", "recovery-cursor-boundaries", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string SparseBrokenRecord(string directory, long size)
    {
        var path = Path.Combine(directory, $"session-{Guid.NewGuid():N}.tcr");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.WriteByte((byte)'{');
        stream.SetLength(size);
        return path;
    }

    private static string CreateRecord(string directory, string name)
    {
        using var writer = new RecoveryStore(directory);
        writer.Save(new ProjectDocument { Name = name }, Array.Empty<RecoveryPropertyDraft>());
        return writer.DraftRecoveryPath;
    }

    [Fact]
    public void CandidateBeyondRemainingByteBudgetIsDeferredWithoutLossOrDuplicate()
    {
        var directory = NewDirectory();
        const long size = 42L * 1024 * 1024;
        var paths = new[] { SparseBrokenRecord(directory, size), SparseBrokenRecord(directory, size) };
        using var cursor = new RecoveryScanCursor(directory, maxBytesPerPage: 80L * 1024 * 1024);

        var first = cursor.ReadNextPage();
        Assert.Single(first.Candidates);
        Assert.Single(first.Errors);
        Assert.True(first.HasMore);
        var second = cursor.ReadNextPage();
        Assert.Single(second.Candidates);
        Assert.Single(second.Errors);
        var seen = first.Candidates.Concat(second.Candidates).Select(c => c.Path).ToArray();
        Assert.Equal(2, seen.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(paths, path => { Assert.Contains(path, seen); Assert.Equal(size, new FileInfo(path).Length); });
        Assert.False(cursor.ReadNextPage().HasMore);
    }

    [Fact]
    public void OversizedRecordIsReportedAndDoesNotBlockLaterRecord()
    {
        var directory = NewDirectory();
        const long oversized = 81L * 1024 * 1024;
        var invalid = SparseBrokenRecord(directory, oversized);
        var valid = CreateRecord(directory, "Sonraki kayıt");
        using var cursor = new RecoveryScanCursor(directory, maxBytesPerPage: 80L * 1024 * 1024);
        var candidates = new List<RecoveryCandidate>();
        var errors = new List<RecoveryScanError>();
        for (var i = 0; i < 4; i++)
        {
            var page = cursor.ReadNextPage();
            candidates.AddRange(page.Candidates); errors.AddRange(page.Errors);
            if (!page.HasMore) break;
            Assert.True(i < 3, "Cursor did not progress past the oversized record.");
        }
        Assert.Contains(candidates, c => c.Path == invalid && !c.IsRecoverable);
        Assert.Contains(errors, e => e.Path == invalid);
        Assert.Contains(candidates, c => c.Path == valid && c.IsRecoverable);
        Assert.Equal(oversized, new FileInfo(invalid).Length);
    }

    [Fact]
    public void ExactlyFullLastPageCanBeFollowedByAnEmptyTerminalPage()
    {
        var directory = NewDirectory();
        var source = CreateRecord(directory, "Tek kayıt");
        using var cursor = new RecoveryScanCursor(directory, maxCandidatesPerPage: 1);
        var first = cursor.ReadNextPage();
        Assert.Equal(source, Assert.Single(first.Candidates).Path);
        Assert.True(first.HasMore);
        var last = cursor.ReadNextPage();
        Assert.Empty(last.Candidates);
        Assert.Empty(last.Errors);
        Assert.False(last.HasMore);
        Assert.False(cursor.ReadNextPage().HasMore);
    }

    [Fact]
    public void CancellationBetweenPagesDisposesCursorAndPreservesUnvisitedRecord()
    {
        var directory = NewDirectory();
        var firstPath = CreateRecord(directory, "Bir");
        var secondPath = CreateRecord(directory, "İki");
        using var cursor = new RecoveryScanCursor(directory, maxCandidatesPerPage: 1);
        Assert.Single(cursor.ReadNextPage().Candidates);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => cursor.ReadNextPage(cancellation.Token));
        Assert.Throws<ObjectDisposedException>(() => cursor.ReadNextPage());
        using var observer = new RecoveryStore(directory);
        Assert.Equal("Bir", observer.LoadCandidate(firstPath).Document.Name);
        Assert.Equal("İki", observer.LoadCandidate(secondPath).Document.Name);
    }
}
