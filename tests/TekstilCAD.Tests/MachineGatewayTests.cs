using System.Text.Json;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public sealed class MachineGatewayTests
{
    private static string Spool()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root missing.");
        return Path.Combine(root.FullName, "work", "machine-tests", Guid.NewGuid().ToString("N"));
    }
    private static ProjectDocument Square() => new()
    { Name = "Calibration", Pieces = [new PatternPiece { Name = "100 mm", Points = [new(0,0), new(100,0), new(100,100), new(0,100)] }] };

    [Fact]
    public void ProfileCannotClaimPhysicalCompatibility()
    { var p = new MachineProfile("Simulator"); Assert.True(p.IsSimulator); Assert.False(p.IsVerified); }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void BadScaleRejectedBeforeWriting(double scale)
    {
        var dir = Spool(); var queue = new SimulationQueue(dir);
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.Prepare(Square(), new("sim", Scale: scale), JobFormat.Hpgl));
        Assert.False(Directory.Exists(dir)); Assert.Empty(queue.Jobs);
    }

    [Theory]
    [InlineData(@"C:\WearCadfe\never-create")]
    [InlineData(@"C:\HPGL\never-create")]
    [InlineData(@"C:\Nester\never-create")]
    [InlineData(@"C:\Nester1\never-create")]
    [InlineData(@"C:\NST\never-create")]
    public void ReferenceDirectoriesRejectedWithoutWriting(string path)
    { Assert.Throws<UnauthorizedAccessException>(() => new SimulationQueue(path)); }

    [Theory]
    [InlineData("relative")]
    [InlineData(@"\\server\share\jobs")]
    [InlineData(@"C:\jobs:stream")]
    public void UnsafeSpoolRejected(string path)
    { Assert.Throws<ArgumentException>(() => new SimulationQueue(path)); }

    [Fact]
    public void DxfPrepareRecordsChecksumAndSimulatorManifest()
    {
        var queue = new SimulationQueue(Spool()); var source = Square();
        var job = queue.Prepare(source, new("Demo", Scale: 2), JobFormat.Dxf);
        Assert.Equal(JobStatus.Prepared, job.Status); Assert.Equal(64, job.Sha256.Length);
        Assert.Single(queue.Jobs); Assert.True(File.Exists(job.ManifestPath));
        var imported = DxfFormat.Import(job.OutputPath).Project;
        Assert.Equal(200, Geometry.Bounds(imported.Pieces[0].Points).Width);
        Assert.Equal(100, Geometry.Bounds(source.Pieces[0].Points).Width);
        using var manifest = JsonDocument.Parse(File.ReadAllText(job.ManifestPath));
        Assert.False(manifest.RootElement.GetProperty("Profile").GetProperty("IsVerified").GetBoolean());
        Assert.Equal(JobStatus.Simulated, queue.Simulate(job.Id).Status);
    }

    [Fact]
    public void HpglPrepareUsesConfiguredUnitsAndIndependentFiles()
    {
        var queue = new SimulationQueue(Spool());
        var first = queue.Prepare(Square(), new("Plot file", UnitsPerMillimeter: 10), JobFormat.Hpgl);
        var second = queue.Prepare(Square(), new("Plot file"), JobFormat.Hpgl);
        Assert.NotEqual(first.OutputPath, second.OutputPath);
        Assert.Contains("1000,1000", File.ReadAllText(first.OutputPath));
        Assert.Equal(JobStatus.Simulated, queue.Simulate(first.Id).Status);
    }

    [Fact]
    public void ChangedFileFailsSimulationAndPersistsFailure()
    {
        var queue = new SimulationQueue(Spool()); var job = queue.Prepare(Square(), new("Simulator"), JobFormat.Dxf);
        File.AppendAllText(job.OutputPath, "tampered");
        var failed = queue.Simulate(job.Id);
        Assert.Equal(JobStatus.Failed, failed.Status); Assert.NotNull(failed.Error);
        Assert.Equal(JobStatus.Failed, queue.Jobs[0].Status);
        Assert.Contains("Failed", File.ReadAllText(job.ManifestPath));
    }

    [Fact]
    public void EmptyDocumentAndUnknownJobAreRejected()
    {
        var queue = new SimulationQueue(Spool());
        Assert.Throws<InvalidDataException>(() => queue.Prepare(new(), new("sim"), JobFormat.Dxf));
        Assert.Throws<KeyNotFoundException>(() => queue.Simulate(Guid.NewGuid()));
    }
}
