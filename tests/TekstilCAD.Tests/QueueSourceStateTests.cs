using TekstilCAD.Core;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public sealed class QueueSourceStateTests
{
    [Fact]
    public void UnconfirmedSnapshotSurvivesRestartAndChecksumVerification()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root missing.");
        var path = Path.Combine(root.FullName, "work", "queue-source-state-tests", Guid.NewGuid().ToString("N"));
        var document = new ProjectDocument { Name = "Snapshot", Pieces = [new PatternPiece { Points = [new(0,0),new(10,0),new(10,10),new(0,10)] }] };
        var queue = new SimulationQueue(path);
        var job = queue.Prepare(document, new MachineProfile("Local"), JobFormat.Dxf, sourceSnapshotUnconfirmed: true);

        var recovered = new SimulationQueue(path);
        Assert.True(Assert.Single(recovered.Jobs).SourceSnapshotUnconfirmed);
        Assert.True(recovered.Simulate(job.Id).SourceSnapshotUnconfirmed);
        Assert.True(Assert.Single(new SimulationQueue(path).Jobs).SourceSnapshotUnconfirmed);

        Assert.False(recovered.ConfirmSourceSnapshot(job.Id).SourceSnapshotUnconfirmed);
        Assert.False(Assert.Single(new SimulationQueue(path).Jobs).SourceSnapshotUnconfirmed);
    }
}
