using System.Text.Json.Nodes;
using TekstilCAD.Core;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public sealed class QueueSourceStateAdversarialTests
{
    private static string Spool()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root missing.");
        return Path.Combine(root.FullName, "work", "queue-source-adversarial-tests", Guid.NewGuid().ToString("N"));
    }

    private static ProjectDocument Square() => new()
    {
        Name = "Snapshot", Pieces = [new PatternPiece { Points = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)] }]
    };

    [Theory]
    [InlineData("null")]
    [InlineData("\"true\"")]
    [InlineData("1")]
    [InlineData("{}")]
    public void MalformedSourceFlagIsIsolatedWithoutChangingValidNeighbor(string value)
    {
        var path = Spool(); var queue = new SimulationQueue(path);
        var broken = queue.Prepare(Square(), new("Simulator"), JobFormat.Dxf, true);
        var valid = queue.Prepare(Square(), new("Simulator"), JobFormat.Dxf, true);
        var json = JsonNode.Parse(File.ReadAllText(broken.ManifestPath))!.AsObject();
        json["SourceSnapshotUnconfirmed"] = JsonNode.Parse(value);
        File.WriteAllText(broken.ManifestPath, json.ToJsonString());
        var before = File.ReadAllBytes(broken.ManifestPath);

        var recovered = new SimulationQueue(path);
        var loaded = Assert.Single(recovered.Jobs);
        Assert.Equal(valid.Id, loaded.Id);
        Assert.True(loaded.SourceSnapshotUnconfirmed);
        Assert.Single(recovered.RecoveryWarnings);
        Assert.Equal(before, File.ReadAllBytes(broken.ManifestPath));
    }

    [Fact]
    public void LegacyMissingSourceFlagDefaultsToConfirmedAndSurvivesVerification()
    {
        var path = Spool(); var queue = new SimulationQueue(path);
        var job = queue.Prepare(Square(), new("Simulator"), JobFormat.Dxf, true);
        var json = JsonNode.Parse(File.ReadAllText(job.ManifestPath))!.AsObject();
        json.Remove("SourceSnapshotUnconfirmed");
        File.WriteAllText(job.ManifestPath, json.ToJsonString());
        var bytes = File.ReadAllBytes(job.ManifestPath);

        var recovered = new SimulationQueue(path);
        Assert.False(Assert.Single(recovered.Jobs).SourceSnapshotUnconfirmed);
        Assert.Empty(recovered.RecoveryWarnings);
        Assert.Equal(bytes, File.ReadAllBytes(job.ManifestPath));
        Assert.False(recovered.Simulate(job.Id).SourceSnapshotUnconfirmed);
        Assert.False(Assert.Single(new SimulationQueue(path).Jobs).SourceSnapshotUnconfirmed);
    }

    [Fact]
    public void FailedChecksumVerificationDoesNotConfirmSource()
    {
        var path = Spool(); var queue = new SimulationQueue(path);
        var job = queue.Prepare(Square(), new("Simulator"), JobFormat.Dxf, true);
        File.AppendAllText(job.OutputPath, "changed");

        var failed = queue.Simulate(job.Id);
        Assert.Equal(JobStatus.Failed, failed.Status);
        Assert.True(failed.SourceSnapshotUnconfirmed);
        var reloaded = Assert.Single(new SimulationQueue(path).Jobs);
        Assert.Equal(JobStatus.Failed, reloaded.Status);
        Assert.True(reloaded.SourceSnapshotUnconfirmed);
    }

    [Fact]
    public void FailedConfirmationPersistenceLeavesMemoryAndDiskUnconfirmed()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows share modes deny replacement while the file is open.
        var path = Spool(); var queue = new SimulationQueue(path);
        var job = queue.Prepare(Square(), new("Simulator"), JobFormat.Dxf, true);
        var before = File.ReadAllBytes(job.ManifestPath);
        using (new FileStream(job.ManifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => queue.ConfirmSourceSnapshot(job.Id));
            Assert.True(error is IOException or UnauthorizedAccessException,
                $"Expected a file access failure, got {error?.GetType().Name ?? "no exception"}.");
        }

        Assert.True(Assert.Single(queue.Jobs).SourceSnapshotUnconfirmed);
        Assert.Equal(before, File.ReadAllBytes(job.ManifestPath));
        Assert.True(Assert.Single(new SimulationQueue(path).Jobs).SourceSnapshotUnconfirmed);
        Assert.False(queue.ConfirmSourceSnapshot(job.Id).SourceSnapshotUnconfirmed);
        Assert.False(Assert.Single(new SimulationQueue(path).Jobs).SourceSnapshotUnconfirmed);
    }
}
