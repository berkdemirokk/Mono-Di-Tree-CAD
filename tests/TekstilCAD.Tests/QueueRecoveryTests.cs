using System.Text.Json;
using System.Text.Json.Nodes;
using TekstilCAD.Core;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public sealed class QueueRecoveryTests
{
    private static string Spool()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root missing.");
        return Path.Combine(root.FullName, "work", "queue-recovery-tests", Guid.NewGuid().ToString("N"));
    }
    private static ProjectDocument Square() => new()
    { Name = "Restart", Pieces = [new PatternPiece { Points = [new(0,0),new(10,0),new(10,10),new(0,10)] }] };
    private static SimulationJob Prepare(SimulationQueue queue) => queue.Prepare(Square(), new("Local simulator"), JobFormat.Dxf);
    private static void Change(SimulationJob job, Action<JsonObject> mutation)
    {
        var json = JsonNode.Parse(File.ReadAllText(job.ManifestPath))!.AsObject();
        mutation(json); File.WriteAllText(job.ManifestPath, json.ToJsonString());
    }

    [Fact]
    public void RestartPreservesAllExistingStatesWithoutRewritingOrAutomaticallyVerifying()
    {
        var path=Spool();var queue=new SimulationQueue(path);
        var prepared=Prepare(queue);var simulated=Prepare(queue);var failed=Prepare(queue);
        queue.Simulate(simulated.Id);
        File.AppendAllText(failed.OutputPath,"changed");queue.Simulate(failed.Id);
        // A prepared output changed after preparation must remain prepared until explicit verification.
        File.AppendAllText(prepared.OutputPath,"also changed");
        var manifestBytes=queue.Jobs.ToDictionary(j=>j.Id,j=>File.ReadAllBytes(j.ManifestPath));
        var recovered=new SimulationQueue(path);
        Assert.Equal(3,recovered.Jobs.Count);Assert.Empty(recovered.RecoveryWarnings);
        Assert.Equal(JobStatus.Prepared,recovered.Jobs.Single(j=>j.Id==prepared.Id).Status);
        Assert.Equal(JobStatus.Simulated,recovered.Jobs.Single(j=>j.Id==simulated.Id).Status);
        Assert.Equal(JobStatus.Failed,recovered.Jobs.Single(j=>j.Id==failed.Id).Status);
        foreach(var job in recovered.Jobs)
        {
            Assert.Equal(manifestBytes[job.Id],File.ReadAllBytes(job.ManifestPath));
            Assert.True(job.Profile.IsSimulator);Assert.False(job.Profile.IsVerified);
        }
    }

    [Fact]
    public void CorruptManifestDoesNotHideValidNeighbor()
    {
        var path=Spool();var queue=new SimulationQueue(path);var broken=Prepare(queue);var good=Prepare(queue);
        File.WriteAllText(broken.ManifestPath,"{ not JSON");
        var recovered=new SimulationQueue(path);
        Assert.Equal(good.Id,Assert.Single(recovered.Jobs).Id);Assert.Single(recovered.RecoveryWarnings);
        Assert.Equal("{ not JSON",File.ReadAllText(broken.ManifestPath));
    }

    [Fact]
    public void MissingArtifactIsFailedInMemoryAndOriginalManifestRemainsUntouched()
    {
        var path=Spool();var queue=new SimulationQueue(path);var job=Prepare(queue);
        var original=File.ReadAllBytes(job.ManifestPath);File.Delete(job.OutputPath);
        var recovered=new SimulationQueue(path);var failed=Assert.Single(recovered.Jobs);
        Assert.Equal(JobStatus.Failed,failed.Status);Assert.NotNull(failed.Error);
        Assert.Single(recovered.RecoveryWarnings);Assert.Equal(original,File.ReadAllBytes(job.ManifestPath));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("version")]
    [InlineData("status")]
    [InlineData("format")]
    [InlineData("hash")]
    [InlineData("count")]
    [InlineData("profile")]
    [InlineData("physicalClaim")]
    [InlineData("outsideOutput")]
    [InlineData("outsideManifest")]
    [InlineData("traversal")]
    public void InvalidManifestIsIsolatedWithoutReadingOrRewritingExternalFiles(string mutation)
    {
        var path=Spool();var queue=new SimulationQueue(path);var job=Prepare(queue);var good=Prepare(queue);
        var outside=Path.Combine(path,"outside.dxf");File.WriteAllText(outside,"outside unchanged");
        Change(job,json=>
        {
            switch(mutation)
            {
                case "id":json["Id"]=Guid.NewGuid().ToString();break;
                case "version":json["ManifestVersion"]=999;break;
                case "status":json["Status"]="PhysicalCompleted";break;
                case "format":json["Format"]=123;break;
                case "hash":json["Sha256"]=new string('Z',64);break;
                case "count":json["PointCount"]=int.MaxValue;break;
                case "profile":json["Profile"]=null;break;
                case "physicalClaim":json["Profile"]!["IsVerified"]=true;break;
                case "outsideOutput":json["OutputPath"]=outside;break;
                case "outsideManifest":json["ManifestPath"]=Path.Combine(path,"manifest.json");break;
                case "traversal":json["OutputPath"]=Path.Combine(Path.GetDirectoryName(job.OutputPath)!,"..",job.Id.ToString("N"),"preview.dxf");break;
            }
        });
        var bytes=File.ReadAllBytes(job.ManifestPath);var recovered=new SimulationQueue(path);
        Assert.Equal(good.Id,Assert.Single(recovered.Jobs).Id);Assert.Single(recovered.RecoveryWarnings);
        Assert.Equal(bytes,File.ReadAllBytes(job.ManifestPath));Assert.Equal("outside unchanged",File.ReadAllText(outside));
    }

    [Fact]
    public void LegacyManifestWithoutVersionLoadsAsVersionOne()
    {
        var path=Spool();var queue=new SimulationQueue(path);var job=Prepare(queue);
        Change(job,json=>json.Remove("ManifestVersion"));
        var recovered=new SimulationQueue(path);
        Assert.Equal(1,Assert.Single(recovered.Jobs).ManifestVersion);Assert.Empty(recovered.RecoveryWarnings);
    }

    [Fact]
    public void OversizedManifestIsRejectedWhileValidNeighborLoads()
    {
        var path=Spool();var queue=new SimulationQueue(path);var large=Prepare(queue);var good=Prepare(queue);
        File.WriteAllText(large.ManifestPath,new string(' ',128*1024+1));
        var recovered=new SimulationQueue(path);
        Assert.Equal(good.Id,Assert.Single(recovered.Jobs).Id);Assert.Single(recovered.RecoveryWarnings);
    }

    [Fact]
    public void ExcessiveJsonDepthIsRejected()
    {
        var path=Spool();var queue=new SimulationQueue(path);var deep=Prepare(queue);
        File.WriteAllText(deep.ManifestPath,new string('[',40)+"0"+new string(']',40));
        var recovered=new SimulationQueue(path);
        Assert.Empty(recovered.Jobs);Assert.Single(recovered.RecoveryWarnings);
    }

    [Fact]
    public void MissingSpoolIsNotCreatedOnReadOnlyRestart()
    {
        var path=Spool();var queue=new SimulationQueue(path);
        Assert.Empty(queue.Jobs);Assert.Empty(queue.RecoveryWarnings);Assert.False(Directory.Exists(path));
    }
}
