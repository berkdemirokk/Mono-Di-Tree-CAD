using TekstilCAD.Core;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public sealed class QueueConcurrencyTests
{
    [Fact]
    public void StaleQueueCannotOverwriteAnotherInstancesConfirmation()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root missing.");
        var path = Path.Combine(root.FullName, "work", "queue-concurrency-tests", Guid.NewGuid().ToString("N"));
        var document = new ProjectDocument { Name = "Snapshot", Pieces = [new PatternPiece { Points = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)] }] };
        var first = new SimulationQueue(path);
        var job = first.Prepare(document, new MachineProfile("Local"), JobFormat.Dxf, sourceSnapshotUnconfirmed: true);
        var stale = new SimulationQueue(path);

        first.ConfirmSourceSnapshot(job.Id);
        var confirmedManifest = File.ReadAllBytes(job.ManifestPath);
        Assert.Throws<InvalidOperationException>(() => stale.Simulate(job.Id));
        Assert.Equal(confirmedManifest, File.ReadAllBytes(job.ManifestPath));
        Assert.True(Assert.Single(stale.Jobs).SourceSnapshotUnconfirmed);
        var recovered = Assert.Single(new SimulationQueue(path).Jobs);
        Assert.False(recovered.SourceSnapshotUnconfirmed);
        Assert.Equal(JobStatus.Prepared, recovered.Status);
    }

    [Fact]
    public void DeletedManifestIsNotRecreatedByStaleMutation()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root missing.");
        var path = Path.Combine(root.FullName, "work", "queue-concurrency-tests", Guid.NewGuid().ToString("N"));
        var document = new ProjectDocument { Name = "Snapshot", Pieces = [new PatternPiece { Points = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)] }] };
        var queue = new SimulationQueue(path);
        var job = queue.Prepare(document, new MachineProfile("Local"), JobFormat.Dxf, sourceSnapshotUnconfirmed: true);
        File.Delete(job.ManifestPath);

        Assert.Throws<InvalidOperationException>(() => queue.ConfirmSourceSnapshot(job.Id));
        Assert.False(File.Exists(job.ManifestPath));
        Assert.True(Assert.Single(queue.Jobs).SourceSnapshotUnconfirmed);
    }
}
