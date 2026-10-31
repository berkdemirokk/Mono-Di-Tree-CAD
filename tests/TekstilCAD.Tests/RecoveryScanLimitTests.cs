using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class RecoveryScanLimitTests
{
    private static string TestDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        return Path.Combine(root.FullName, "work", "recovery-scan-limit-tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void CandidateCountLimitReportsThatFilesWereNotScanned()
    {
        var directory = TestDirectory();
        Directory.CreateDirectory(directory);
        for (var i = 0; i < 1025; i++)
            File.WriteAllText(Path.Combine(directory, $"session-{Guid.NewGuid():N}.{(i % 2 == 0 ? "tcad" : "tcr")}"), "{}");
        var result = RecoveryStore.ScanDirectory(directory);
        Assert.True(result.Candidates.Count <= 1024);
        Assert.Contains(result.Errors, e => e.Message.Contains("kayıt sayısı sınırına", StringComparison.Ordinal));
    }

    [Fact]
    public void AggregateByteLimitReportsIncompleteScanWithoutReadingRemainingFile()
    {
        var directory = TestDirectory();
        Directory.CreateDirectory(directory);
        var paths = Enumerable.Range(0, 2)
            .Select(_ => Path.Combine(directory, $"session-{Guid.NewGuid():N}.tcr"))
            .ToArray();
        try
        {
            // Each file fits the 80 MB per-record limit, but together they exceed
            // the 128 MB aggregate budget. Enumeration order is unspecified.
            foreach (var path in paths)
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                    stream.SetLength(65L * 1024 * 1024);
            var result = RecoveryStore.ScanDirectory(directory);
            var inspected = Assert.Single(result.Candidates);
            Assert.NotNull(inspected.Error); // Zero-filled data is not a valid envelope.
            var deferred = Assert.Single(result.Errors, e => e.Message.Contains("toplam boyut sınırına", StringComparison.Ordinal));
            Assert.Contains(deferred.Path, paths);
            Assert.NotEqual(inspected.Path, deferred.Path);
            Assert.All(paths, path => Assert.True(File.Exists(path)));
        }
        finally { foreach (var path in paths) if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void OversizedRecordIsReportedWithoutExhaustingAggregateBudget()
    {
        var directory = TestDirectory();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"session-{Guid.NewGuid():N}.tcr");
        try
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                stream.SetLength(128L * 1024 * 1024 + 1);
            var result = RecoveryStore.ScanDirectory(directory);
            var candidate = Assert.Single(result.Candidates);
            Assert.Contains("boyut sınırını aşıyor", candidate.Error);
            Assert.DoesNotContain(result.Errors, e => e.Message.Contains("toplam boyut sınırına", StringComparison.Ordinal));
            Assert.True(File.Exists(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}