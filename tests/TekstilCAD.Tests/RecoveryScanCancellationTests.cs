using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class RecoveryScanCancellationTests
{
    private static string TestDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        return Path.Combine(root.FullName, "work", "recovery-scan-cancellation-tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void CancelledScanPreservesRecoveryAndLaterScanCanAcquireLease()
    {
        var directory = TestDirectory();
        string path;
        using (var writer = new RecoveryStore(directory))
        {
            writer.Save(new ProjectDocument { Name = "Kurtarılacak çalışma" });
            path = writer.RecoveryPath;
        }
        var original = File.ReadAllBytes(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            RecoveryStore.ScanDirectory(directory, cancellationToken: cancellation.Token));
        Assert.Equal(original, File.ReadAllBytes(path));

        using var observer = new RecoveryStore(directory);
        var candidate = Assert.Single(observer.Scan().Candidates);
        Assert.Equal(path, candidate.Path);
        Assert.Equal("Kurtarılacak çalışma", observer.LoadCandidate(path).Document.Name);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void ExistingCallAndUncancelledTokenReturnSameCandidates()
    {
        var directory = TestDirectory();
        string path;
        using (var writer = new RecoveryStore(directory))
        {
            writer.Save(new ProjectDocument { Name = "Eski çağrı" });
            path = writer.RecoveryPath;
        }
        var original = File.ReadAllBytes(path);
        var legacy = RecoveryStore.ScanDirectory(directory);
        var explicitToken = RecoveryStore.ScanDirectory(directory, cancellationToken: CancellationToken.None);

        Assert.Empty(legacy.Errors);
        Assert.Empty(explicitToken.Errors);
        Assert.Equal(path, Assert.Single(legacy.Candidates).Path);
        Assert.Equal(Assert.Single(legacy.Candidates), Assert.Single(explicitToken.Candidates));
        Assert.Equal(original, File.ReadAllBytes(path));
    }
}
