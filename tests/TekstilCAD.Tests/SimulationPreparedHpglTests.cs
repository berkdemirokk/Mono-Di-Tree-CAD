using TekstilCAD.Core;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public sealed class SimulationPreparedHpglTests
{
    private static string Spool()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        if(root is null)throw new InvalidOperationException("Repository root not found.");
        return Path.Combine(root.FullName,"work","prepared-machine-tests",Guid.NewGuid().ToString("N"));
    }
    private static ProjectDocument Source()=>new() { Name="Seçim",Pieces=[new PatternPiece { Name="Ön",Points=[new(0,0),new(10,0),new(10,10),new(0,10)] }] };

    [Fact]
    public void ReviewDoesNotWriteAndCommittedManifestMatchesReviewedScaledBytes()
    {
        var spool=Spool();var queue=new SimulationQueue(spool);var source=Source();var profile=new MachineProfile("Yerel",40,2);
        var proposal=queue.PrepareHpglReview(source,profile);
        Assert.False(Directory.Exists(spool));Assert.Empty(queue.Jobs);
        Assert.Contains("800,0",proposal.Output.Text);Assert.Equal(40,proposal.Output.UnitsPerMillimeter);
        var job=queue.CommitPreparedHpgl(proposal,source,profile,true);
        Assert.Equal(proposal.Output.Text,File.ReadAllText(job.OutputPath));Assert.True(job.SourceSnapshotUnconfirmed);
        Assert.Equal(source.Name,job.ProjectName);Assert.Equal(profile,job.Profile);Assert.Equal(1,job.PieceCount);Assert.Equal(4,job.PointCount);
        var loaded=Assert.Single(new SimulationQueue(spool).Jobs);
        Assert.Equal(job.Sha256,loaded.Sha256);Assert.Equal(job.Id,loaded.Id);Assert.True(loaded.SourceSnapshotUnconfirmed);
        Assert.False(loaded.Profile.IsVerified);Assert.True(loaded.Profile.IsSimulator);
    }

    [Fact]
    public void QueueSourceAndProfileAreBoundBeforeAnyWrite()
    {
        var spool=Spool();var queue=new SimulationQueue(spool);var other=new SimulationQueue(Spool());
        var source=Source();var profile=new MachineProfile("Yerel",40,2);var proposal=queue.PrepareHpglReview(source,profile);
        Assert.Throws<InvalidOperationException>(()=>other.CommitPreparedHpgl(proposal,source,profile));
        Assert.Throws<InvalidOperationException>(()=>queue.CommitPreparedHpgl(proposal,source,profile with { Scale=1 }));
        Assert.Throws<InvalidOperationException>(()=>queue.CommitPreparedHpgl(proposal,source,profile with { UnitsPerMillimeter=41 }));
        source.Pieces[0].Points[1]=new Point2(11,0);
        Assert.Throws<InvalidOperationException>(()=>queue.CommitPreparedHpgl(proposal,source,profile));
        Assert.False(Directory.Exists(spool));Assert.Empty(queue.Jobs);
        source.Pieces[0].Points[1]=new Point2(10,0);
        var job=queue.CommitPreparedHpgl(proposal,source,profile);
        Assert.True(File.Exists(job.OutputPath));
        Assert.Throws<InvalidOperationException>(()=>queue.CommitPreparedHpgl(proposal,source,profile));
        Assert.Single(queue.Jobs);
    }

    [Fact]
    public void CancelBeforeReviewOrCommitDoesNotCreateSpool()
    {
        var spool=Spool();var queue=new SimulationQueue(spool);var source=Source();var profile=new MachineProfile("Yerel");
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>queue.PrepareHpglReview(source,profile,cancellation.Token));
        var proposal=queue.PrepareHpglReview(source,profile);
        Assert.Throws<OperationCanceledException>(()=>queue.CommitPreparedHpgl(proposal,source,profile,cancellationToken:cancellation.Token));
        Assert.False(Directory.Exists(spool));Assert.Empty(queue.Jobs);
    }
}
