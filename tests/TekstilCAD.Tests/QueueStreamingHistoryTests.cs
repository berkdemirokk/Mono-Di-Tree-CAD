using TekstilCAD.Core;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public sealed class QueueStreamingHistoryTests
{
    private static ProjectDocument Document() => new() { Name = "stream", Pieces = [new PatternPiece { Closed = false, Points = [new(0, 0), new(10, 0)] }] };
    [Fact]
    public void StreamPagesExposeInvalidFoldersAndDoNotAccumulateJobs()
    {
        var path = Path.Combine(Path.GetTempPath(), "queue-stream-" + Guid.NewGuid().ToString("N"));
        try
        {
            var queue = new SimulationQueue(path, streamingHistory: true);
            var a = queue.Prepare(Document(), new MachineProfile("test"), JobFormat.Dxf);
            var b = queue.Prepare(Document(), new MachineProfile("test"), JobFormat.Dxf);
            Directory.CreateDirectory(Path.Combine(path, "invalid"));
            Assert.Empty(queue.Jobs);
            using var scan = queue.StartHistoryScan();
            var entries = new List<SimulationQueue.HistoryEntry>();
            SimulationQueue.HistoryPage page;
            do { page = scan.ReadNextPage(1); entries.AddRange(page.Entries); Assert.Null(page.Error); } while (!page.Complete);
            Assert.Equal(3, entries.Count);
            Assert.Contains(entries, entry => entry.Job?.Id == a.Id);
            Assert.Contains(entries, entry => entry.Job?.Id == b.Id);
            Assert.Contains(entries, entry => entry.Job is null && entry.Error is not null);
            Assert.Empty(queue.Jobs);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
    [Fact]
    public void StaleManifestCannotBeConfirmedAndCancellationDoesNotDeleteFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), "queue-stream-" + Guid.NewGuid().ToString("N"));
        try
        {
            var queue = new SimulationQueue(path, streamingHistory: true);
            var job = queue.Prepare(Document(), new MachineProfile("test"), JobFormat.Dxf, sourceSnapshotUnconfirmed: true);
            var entry = queue.GetPreparedHistoryEntry(job);
            File.AppendAllText(job.ManifestPath, " ");
            Assert.Throws<InvalidOperationException>(() => queue.ConfirmHistoryEntry(entry));
            Assert.Throws<InvalidOperationException>(() => queue.GetPreparedHistoryEntry(job));
            using var scan = queue.StartHistoryScan();
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() => scan.ReadNextPage(1, cancelled.Token));
            var page = scan.ReadNextPage(1);
            Assert.Single(page.Entries);
            Assert.True(File.Exists(job.OutputPath));
            Assert.True(File.Exists(job.ManifestPath));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangeAfterCommitIsNotAdoptedAsTheWrittenVersion(bool simulate)
    {
        var path = Path.Combine(Path.GetTempPath(), "queue-stream-" + Guid.NewGuid().ToString("N"));
        try
        {
            var queue = new SimulationQueue(path, streamingHistory: true);
            var job = queue.Prepare(Document(), new MachineProfile("test"), JobFormat.Dxf, sourceSnapshotUnconfirmed: true);
            var entry = queue.GetPreparedHistoryEntry(job);
            var hook = typeof(SimulationQueue).GetProperty("AfterHistoryCommitForVerification", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            hook.SetValue(queue, (Action<string>)(manifest => File.AppendAllText(manifest, " ")));
            var error = Assert.Throws<InvalidOperationException>(() =>
            {
                if (simulate) queue.SimulateHistoryEntry(entry);
                else queue.ConfirmHistoryEntry(entry);
            });
            Assert.Contains("uygulanmış olabilir", error.Message);
            Assert.True(File.Exists(job.ManifestPath));
            Assert.True(File.Exists(job.OutputPath));
            // The change occurred after the commit; the caller must inspect the
            // new record rather than treating this failure as a failed write.
            hook.SetValue(queue, null);
            var observed = queue.GetHistoryEntry(job.Id);
            Assert.NotNull(observed.Job);
            if (simulate) Assert.Equal(JobStatus.Simulated, observed.Job!.Status);
            else Assert.False(observed.Job!.SourceSnapshotUnconfirmed);
            Assert.NotEqual(entry.ManifestHash, observed.ManifestHash);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
}
