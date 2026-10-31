using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TekstilCAD.Core;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public sealed class ImmutableQueueIntegrationTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };
    private static ProjectDocument Document() => new() { Name = "Immutable integration", Pieces = [new PatternPiece { Closed = false, Points = [new(0, 0), new(10, 0)] }] };
    private static string NewDirectory()
    {
        var path = Path.Combine(@"C:\Projects\TekstilCAD\work", "immutable-queue-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }
    private static void Clean(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); }
    private static byte[] Bytes(SimulationJob job) => JsonSerializer.SerializeToUtf8Bytes(job, Options);
    private static string JobDirectory(SimulationJob job) => Path.GetDirectoryName(job.ManifestPath)!;
    private static string[] Versions(SimulationJob job) => Directory.GetFiles(JobDirectory(job), "history-*.json");

    [Fact]
    public void StreamingReopenReadsVerifiedVersionsAndKeepsLegacyManifestBytes()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = NewDirectory();
        try
        {
            var queue = new SimulationQueue(path, streamingHistory: true);
            var job = queue.Prepare(Document(), new MachineProfile("test"), JobFormat.Dxf, sourceSnapshotUnconfirmed: true);
            var original = File.ReadAllBytes(job.ManifestPath);
            var history = new ImmutableJobHistory(JobDirectory(job));
            var first = history.Append(history.Inspect().HeadHash!, Bytes(job with { SourceSnapshotUnconfirmed = false }));
            var latest = job with { SourceSnapshotUnconfirmed = false, Status = JobStatus.Simulated, Error = null };
            history.Append(first.HeadHash!, Bytes(latest));
            var reopened = new SimulationQueue(path, streamingHistory: true);
            var entry = reopened.GetHistoryEntry(job.Id);
            Assert.NotNull(entry.Job);
            Assert.Null(entry.Error);
            Assert.False(entry.Job!.SourceSnapshotUnconfirmed);
            Assert.Equal(JobStatus.Simulated, entry.Job.Status);
            Assert.Equal(original, File.ReadAllBytes(job.ManifestPath));
            Assert.True(File.Exists(job.OutputPath));
        }
        finally { Clean(path); }
    }

    [Fact]
    public void UnsupportedUpdatesFailClosedAndDoNotChangeLegacyManifest()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = NewDirectory();
        try
        {
            var queue = new SimulationQueue(path, streamingHistory: true);
            var job = queue.Prepare(Document(), new MachineProfile("test"), JobFormat.Dxf, sourceSnapshotUnconfirmed: true);
            var original = File.ReadAllBytes(job.ManifestPath);
            var entry = queue.GetPreparedHistoryEntry(job);
            Assert.Throws<InvalidOperationException>(() => queue.ConfirmHistoryEntry(entry));
            Assert.Throws<InvalidOperationException>(() => queue.SimulateHistoryEntry(entry));
            Assert.Equal(original, File.ReadAllBytes(job.ManifestPath));
            Assert.Empty(Versions(job));
            Assert.True(File.Exists(job.OutputPath));
        }
        finally { Clean(path); }
    }

    [Fact]
    public void StaleSelectionAndExternallyModifiedBaseCannotBeApplied()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = NewDirectory();
        try
        {
            var queue = new SimulationQueue(path, streamingHistory: true);
            var job = queue.Prepare(Document(), new MachineProfile("test"), JobFormat.Dxf, sourceSnapshotUnconfirmed: true);
            var entry = queue.GetPreparedHistoryEntry(job);
            var original = File.ReadAllBytes(job.ManifestPath);
            var history = new ImmutableJobHistory(JobDirectory(job));
            history.Append(history.Inspect().HeadHash!, Bytes(job with { SourceSnapshotUnconfirmed = false }));
            var version = Assert.Single(Versions(job)); var versionBytes = File.ReadAllBytes(version);
            Assert.Throws<InvalidOperationException>(() => queue.SimulateHistoryEntry(entry));
            File.AppendAllText(job.ManifestPath, " ");
            var changedBase = File.ReadAllBytes(job.ManifestPath);
            var invalid = new SimulationQueue(path, streamingHistory: true).GetHistoryEntry(job.Id);
            Assert.Null(invalid.Job); Assert.NotNull(invalid.Error);
            Assert.Equal(versionBytes, File.ReadAllBytes(version));
            Assert.Equal(changedBase, File.ReadAllBytes(job.ManifestPath));
            Assert.NotEqual(original.Length, changedBase.Length);
        }
        finally { Clean(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BrokenOrInterruptedEventShowsErrorAndLeavesEvidence(bool partial)
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = NewDirectory();
        try
        {
            var queue = new SimulationQueue(path, streamingHistory: true);
            var job = queue.Prepare(Document(), new MachineProfile("test"), JobFormat.Dxf);
            var original = File.ReadAllBytes(job.ManifestPath);
            var eventPath = partial
                ? Path.Combine(JobDirectory(job), ".history-" + Guid.NewGuid().ToString("N") + ".tmp")
                : Path.Combine(JobDirectory(job), "history-" + new string('A', 64) + ".json");
            File.WriteAllText(eventPath, "broken");
            var entry = new SimulationQueue(path, streamingHistory: true).GetHistoryEntry(job.Id);
            Assert.Null(entry.Job); Assert.NotNull(entry.Error);
            Assert.Equal("broken", File.ReadAllText(eventPath));
            Assert.Equal(original, File.ReadAllBytes(job.ManifestPath));
            Assert.True(File.Exists(job.OutputPath));
        }
        finally { Clean(path); }
    }

    [Fact]
    public void BranchedHistoryIsNotPresentedAsOneSuccessfulJob()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = NewDirectory();
        using var barrier = new Barrier(2);
        try
        {
            var queue = new SimulationQueue(path, streamingHistory: true);
            var job = queue.Prepare(Document(), new MachineProfile("test"), JobFormat.Dxf, sourceSnapshotUnconfirmed: true);
            var original = File.ReadAllBytes(job.ManifestPath);
            var left = new ImmutableJobHistory(JobDirectory(job)); var right = new ImmutableJobHistory(JobDirectory(job));
            var head = left.Inspect().HeadHash!;
            var hook = typeof(ImmutableJobHistory).GetProperty("BeforePublishForVerification", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Action wait = () => { if (!barrier.SignalAndWait(TimeSpan.FromSeconds(15))) throw new TimeoutException(); };
            hook.SetValue(left, wait); hook.SetValue(right, wait);
            var one = Task.Run(() => left.Append(head, Bytes(job with { SourceSnapshotUnconfirmed = false })));
            var two = Task.Run(() => right.Append(head, Bytes(job with { Status = JobStatus.Failed, Error = "failed" })));
            Task.WaitAll(one, two);
            var entry = new SimulationQueue(path, streamingHistory: true).GetHistoryEntry(job.Id);
            Assert.Null(entry.Job); Assert.NotNull(entry.Error);
            Assert.Equal(2, Versions(job).Length);
            Assert.Equal(original, File.ReadAllBytes(job.ManifestPath));
        }
        finally { Clean(path); }
    }

    [Fact]
    public void LegacyQueueDoesNotSilentlyReplaceJobWithImmutableHistory()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = NewDirectory();
        try
        {
            var streaming = new SimulationQueue(path, streamingHistory: true);
            var job = streaming.Prepare(Document(), new MachineProfile("test"), JobFormat.Dxf);
            var original = File.ReadAllBytes(job.ManifestPath);
            var history = new ImmutableJobHistory(JobDirectory(job));
            history.Append(history.Inspect().HeadHash!, Bytes(job with { Status = JobStatus.Simulated }));
            var version = Assert.Single(Versions(job)); var versionBytes = File.ReadAllBytes(version);
            var legacy = new SimulationQueue(path);
            Assert.DoesNotContain(legacy.Jobs, item => item.Id == job.Id);
            Assert.NotEmpty(legacy.RecoveryWarnings);
            Assert.ThrowsAny<Exception>(() => legacy.Simulate(job.Id));
            Assert.Equal(original, File.ReadAllBytes(job.ManifestPath));
            Assert.Equal(versionBytes, File.ReadAllBytes(version));
        }
        finally { Clean(path); }
    }
}
