using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public sealed class ImmutableJobHistoryTests
{
    private static readonly byte[] Original = Encoding.UTF8.GetBytes("{\"status\":\"prepared\"}");
    private static readonly byte[] First = Encoding.UTF8.GetBytes("{\"status\":\"confirmed\"}");
    private static readonly byte[] Second = Encoding.UTF8.GetBytes("{\"status\":\"simulated\"}");
    private static string NewDirectory()
    {
        var path = Path.Combine(@"C:\Projects\TekstilCAD\work", "immutable-history-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path, "manifest.json"), Original);
        return path;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string[] Records(string path) => Directory.GetFiles(path, "history-*.json");
    private static void Clean(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); }

    [Fact]
    public void AppendsPreserveOriginalAndReadLatestPayloadAfterReopen()
    {
        var path = NewDirectory();
        try
        {
            var history = new ImmutableJobHistory(path);
            var initial = history.Inspect();
            Assert.Equal(ImmutableJobHistory.InspectionStatus.Valid, initial.Status);
            Assert.Equal(Hash(Original), initial.BaseHash);
            Assert.Equal(initial.BaseHash, initial.HeadHash);
            Assert.Equal(Original, initial.Payload);
            var first = history.Append(initial.HeadHash!, First);
            Assert.Equal(ImmutableJobHistory.InspectionStatus.Valid, first.Status);
            var second = history.Append(first.HeadHash!, Second);
            Assert.Equal(ImmutableJobHistory.InspectionStatus.Valid, second.Status);
            var reopened = new ImmutableJobHistory(path).Inspect();
            Assert.Equal(ImmutableJobHistory.InspectionStatus.Valid, reopened.Status);
            Assert.Equal(second.HeadHash, reopened.HeadHash);
            Assert.Equal(Second, reopened.Payload);
            Assert.Equal(Original, File.ReadAllBytes(Path.Combine(path, "manifest.json")));
            Assert.Equal(2, Records(path).Length);
        }
        finally { Clean(path); }
    }

    [Fact]
    public void StaleHeadCannotOverwriteACommittedRecord()
    {
        var path = NewDirectory();
        try
        {
            var history = new ImmutableJobHistory(path);
            var oldHead = history.Inspect().HeadHash!;
            var saved = history.Append(oldHead, First);
            var before = Records(path).ToDictionary(p => p, File.ReadAllBytes);
            Assert.Throws<InvalidOperationException>(() => history.Append(oldHead, Second));
            var after = history.Inspect();
            Assert.Equal(ImmutableJobHistory.InspectionStatus.Valid, after.Status);
            Assert.Equal(saved.HeadHash, after.HeadHash);
            Assert.Equal(First, after.Payload);
            Assert.Equal(before.Count, Records(path).Length);
            foreach (var pair in before) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
            Assert.Equal(Original, File.ReadAllBytes(Path.Combine(path, "manifest.json")));
        }
        finally { Clean(path); }
    }

    [Fact]
    public void RecordLimitRejectsAnotherAppendWithoutChangingExistingHistory()
    {
        var path = NewDirectory();
        try
        {
            var history = new ImmutableJobHistory(path);
            var first = history.Append(history.Inspect().HeadHash!, First, maxRecords: 1);
            var record = Assert.Single(Records(path));
            var before = File.ReadAllBytes(record);
            Assert.Throws<InvalidOperationException>(() => history.Append(first.HeadHash!, Second, maxRecords: 1));
            Assert.Equal(before, File.ReadAllBytes(record));
            Assert.Single(Records(path));
            Assert.Equal(first.HeadHash, history.Inspect(maxRecords: 1).HeadHash);
        }
        finally { Clean(path); }
    }

    [Fact]
    public void ChangedBaseIsNeverSilentlyAcceptedAsTheExistingHistory()
    {
        var path = NewDirectory();
        try
        {
            var history = new ImmutableJobHistory(path);
            history.Append(history.Inspect().HeadHash!, First);
            var records = Records(path).ToDictionary(p => p, File.ReadAllBytes);
            File.WriteAllBytes(Path.Combine(path, "manifest.json"), Second);
            Assert.NotEqual(ImmutableJobHistory.InspectionStatus.Valid, history.Inspect().Status);
            foreach (var pair in records) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
            Assert.Equal(Second, File.ReadAllBytes(Path.Combine(path, "manifest.json")));
        }
        finally { Clean(path); }
    }

    [Fact]
    public void MissingParentOrCorruptedRecordCannotBeReportedAsValid()
    {
        var path = NewDirectory();
        try
        {
            var history = new ImmutableJobHistory(path);
            var first = history.Append(history.Inspect().HeadHash!, First);
            var parent = Assert.Single(Records(path));
            history.Append(first.HeadHash!, Second);
            var parentBytes = File.ReadAllBytes(parent);
            File.Delete(parent);
            Assert.NotEqual(ImmutableJobHistory.InspectionStatus.Valid, history.Inspect().Status);
            Assert.Single(Records(path));
            File.WriteAllBytes(parent, parentBytes);
            Assert.Equal(ImmutableJobHistory.InspectionStatus.Valid, history.Inspect().Status);
            File.WriteAllText(parent, "{broken");
            Assert.NotEqual(ImmutableJobHistory.InspectionStatus.Valid, history.Inspect().Status);
            Assert.Equal(2, Records(path).Length);
            Assert.Equal(Original, File.ReadAllBytes(Path.Combine(path, "manifest.json")));
        }
        finally { Clean(path); }
    }

    [Fact]
    public void InterruptedTemporaryFileAndMissingBaseAreReportedAndRetained()
    {
        var path = NewDirectory();
        try
        {
            var history = new ImmutableJobHistory(path);
            var temporary = Path.Combine(path, ".history-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(temporary, "partial");
            Assert.NotEqual(ImmutableJobHistory.InspectionStatus.Valid, history.Inspect().Status);
            Assert.True(File.Exists(temporary));
            File.Delete(temporary);
            File.Delete(Path.Combine(path, "manifest.json"));
            Assert.NotEqual(ImmutableJobHistory.InspectionStatus.Valid, history.Inspect().Status);
        }
        finally { Clean(path); }
    }

    [Fact]
    public void CancellationDoesNotAlterOriginalOrCommittedRecords()
    {
        var path = NewDirectory();
        try
        {
            var history = new ImmutableJobHistory(path);
            var saved = history.Append(history.Inspect().HeadHash!, First);
            var before = Records(path).ToDictionary(p => p, File.ReadAllBytes);
            using var source = new CancellationTokenSource(); source.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => history.Inspect(cancellationToken: source.Token));
            Assert.ThrowsAny<OperationCanceledException>(() => history.Append(saved.HeadHash!, Second, cancellationToken: source.Token));
            Assert.Equal(Original, File.ReadAllBytes(Path.Combine(path, "manifest.json")));
            Assert.Equal(before.Count, Records(path).Length);
            foreach (var pair in before) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
            Assert.Equal(saved.HeadHash, history.Inspect().HeadHash);
        }
        finally { Clean(path); }
    }

    [Fact]
    public void CancellationAfterTemporaryWriteRetainsEvidenceAndOriginal()
    {
        var path = NewDirectory();
        try
        {
            var history = new ImmutableJobHistory(path);
            var head = history.Inspect().HeadHash!;
            using var source = new CancellationTokenSource();
            var hook = typeof(ImmutableJobHistory).GetProperty("AfterTempWriteForVerification", BindingFlags.Instance | BindingFlags.NonPublic)!;
            hook.SetValue(history, (Action)(() => source.Cancel()));
            Assert.ThrowsAny<OperationCanceledException>(() => history.Append(head, First, cancellationToken: source.Token));
            Assert.Equal(Original, File.ReadAllBytes(Path.Combine(path, "manifest.json")));
            Assert.Empty(Records(path));
            Assert.Single(Directory.GetFiles(path, ".history-*.tmp"));
            Assert.NotEqual(ImmutableJobHistory.InspectionStatus.Valid, history.Inspect().Status);
        }
        finally { Clean(path); }
    }

    [Fact]
    public void ConcurrentChildrenArePreservedAndReportedAsConflict()
    {
        var path = NewDirectory();
        using var barrier = new Barrier(2);
        try
        {
            var left = new ImmutableJobHistory(path); var right = new ImmutableJobHistory(path);
            var head = left.Inspect().HeadHash!;
            var hook = typeof(ImmutableJobHistory).GetProperty("BeforePublishForVerification", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Action wait = () => { if (!barrier.SignalAndWait(TimeSpan.FromSeconds(15))) throw new TimeoutException(); };
            hook.SetValue(left, wait); hook.SetValue(right, wait);
            var one = Task.Run(() => left.Append(head, First));
            var two = Task.Run(() => right.Append(head, Second));
            Task.WaitAll(one, two);
            Assert.Equal(ImmutableJobHistory.InspectionStatus.Conflict, left.Inspect().Status);
            Assert.Equal(2, Records(path).Length);
            Assert.Equal(Original, File.ReadAllBytes(Path.Combine(path, "manifest.json")));
        }
        finally { Clean(path); }
    }

    [Fact]
    public void WindowsOpenTemporaryFileCannotBeReplacedBeforePublication()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = NewDirectory();
        try
        {
            var history = new ImmutableJobHistory(path);
            var head = history.Inspect().HeadHash!;
            var displaced = Path.Combine(path, "displaced.tmp");
            string? temporary = null;
            var hook = typeof(ImmutableJobHistory).GetProperty("AfterTempWriteForVerification", BindingFlags.Instance | BindingFlags.NonPublic)!;
            hook.SetValue(history, (Action)(() =>
            {
                temporary = Assert.Single(Directory.GetFiles(path, ".history-*.tmp"));
                Assert.ThrowsAny<IOException>(() => File.Move(temporary, displaced));
                Assert.ThrowsAny<IOException>(() => File.Delete(temporary));
                Assert.ThrowsAny<IOException>(() => File.WriteAllBytes(temporary, Second));
            }));
            var published = history.Append(head, First);
            Assert.Equal(ImmutableJobHistory.InspectionStatus.Valid, published.Status);
            Assert.Equal(First, published.Payload);
            Assert.False(File.Exists(displaced));
            Assert.False(File.Exists(temporary));
            Assert.Single(Records(path));
            Assert.Equal(Original, File.ReadAllBytes(Path.Combine(path, "manifest.json")));
        }
        finally { Clean(path); }
    }

    [Fact]
    public void WindowsPublishCollisionPreservesExistingTargetAndPriorHistory()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = NewDirectory();
        try
        {
            var history = new ImmutableJobHistory(path);
            var first = history.Append(history.Inspect().HeadHash!, First);
            var previous = Assert.Single(Records(path));
            var previousBytes = File.ReadAllBytes(previous);
            var sentinel = Encoding.UTF8.GetBytes("existing target must remain untouched");
            string? target = null;
            var hook = typeof(ImmutableJobHistory).GetProperty("BeforeHandlePublishForVerification", BindingFlags.Instance | BindingFlags.NonPublic)!;
            hook.SetValue(history, (Action<string, string>)((_, destination) =>
            {
                target = destination;
                using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                file.Write(sentinel);
            }));
            Assert.ThrowsAny<IOException>(() => history.Append(first.HeadHash!, Second));
            Assert.NotNull(target);
            Assert.Equal(sentinel, File.ReadAllBytes(target!));
            Assert.Equal(previousBytes, File.ReadAllBytes(previous));
            Assert.Equal(Original, File.ReadAllBytes(Path.Combine(path, "manifest.json")));
            Assert.NotEqual(ImmutableJobHistory.InspectionStatus.Valid, history.Inspect().Status);
        }
        finally { Clean(path); }
    }
}
