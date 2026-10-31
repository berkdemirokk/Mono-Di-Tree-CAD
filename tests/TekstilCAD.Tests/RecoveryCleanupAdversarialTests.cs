using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class RecoveryCleanupAdversarialTests
{
    private static string TestDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        return Path.Combine(root.FullName, "work", "recovery-cleanup-adversarial", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void ClearOwnReportsMissingAndDoesNotDeleteAnotherSession()
    {
        var directory = TestDirectory();
        using var first = new RecoveryStore(directory);
        using var second = new RecoveryStore(directory);
        Assert.False(first.ClearOwn());
        first.Save(new ProjectDocument(), Array.Empty<RecoveryPropertyDraft>());
        second.Save(new ProjectDocument(), Array.Empty<RecoveryPropertyDraft>());
        Assert.True(first.ClearOwn());
        Assert.False(first.ClearOwn());
        Assert.True(File.Exists(second.DraftRecoveryPath));
    }

    [Fact]
    public void LockedOwnSnapshotRaisesAnErrorAndRemainsIntact()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = TestDirectory();
        using var store = new RecoveryStore(directory);
        store.Save(new ProjectDocument(), Array.Empty<RecoveryPropertyDraft>());
        var bytes = File.ReadAllBytes(store.DraftRecoveryPath);
        using (new FileStream(store.DraftRecoveryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => store.ClearOwn());
            Assert.True(error is IOException or UnauthorizedAccessException);
        }
        Assert.Equal(bytes, File.ReadAllBytes(store.DraftRecoveryPath));
        Assert.True(store.ClearOwn());
    }

    [Fact]
    public void RetainedLeaseFilesDoNotHideRecoverableSnapshot()
    {
        var directory = TestDirectory();
        Directory.CreateDirectory(directory);
        for (int i = 0; i < 4100; i++)
            File.WriteAllBytes(Path.Combine(directory, $"session-{Guid.NewGuid():N}.lock"), []);
        string snapshot;
        using (var crashed = new RecoveryStore(directory))
        {
            crashed.Save(new ProjectDocument(), Array.Empty<RecoveryPropertyDraft>());
            snapshot = crashed.DraftRecoveryPath;
        }
        var result = RecoveryStore.ScanDirectory(directory);
        Assert.Contains(result.Candidates, c => c.Path == snapshot && c.IsRecoverable);
        Assert.DoesNotContain(result.Errors, e => e.Message.Contains("dosya giriş sınırına", StringComparison.Ordinal));
    }
}
