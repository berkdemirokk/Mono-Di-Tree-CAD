using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using TekstilCAD.Core;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public sealed class VersionedQueueTests
{
    private static string NewDirectory()
    {
        var path=Path.Combine(@"C:\Projects\TekstilCAD\work","versioned-queue-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }
    private static void Clean(string path) { if(Directory.Exists(path)) Directory.Delete(path,true); }
    private static ProjectDocument Document() => new() { Name="Sınama",Pieces=[new PatternPiece { Name="Ön",Closed=false,Points=[new(0,0),new(10,5)] }] };
    private static readonly MachineProfile Profile=new("Test");
    private static Dictionary<string,byte[]> Snapshot(string path) => Directory.GetFiles(path,"*",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
    private static void AssertUnchanged(Dictionary<string,byte[]> before,string path)
    {
        var after=Snapshot(path);Assert.Equal(before.Count,after.Count);
        foreach(var item in before) { Assert.True(after.ContainsKey(item.Key));Assert.Equal(item.Value,after[item.Key]); }
    }

    [Theory]
    [InlineData(JobFormat.Dxf)]
    [InlineData(JobFormat.Hpgl)]
    public void PreparationConfirmationAndSimulationSurviveReopenWithoutChangingBase(JobFormat format)
    {
        if(!OperatingSystem.IsWindows())return;
        var path=NewDirectory();
        try
        {
            var queue=SimulationQueue.OpenVersionedV2(path);var doc=Document();
            var job=format==JobFormat.Hpgl
                ? queue.CommitPreparedHpgl(queue.PrepareHpglReview(doc,Profile),doc,Profile,sourceSnapshotUnconfirmed:true)
                : queue.Prepare(doc,Profile,format,sourceSnapshotUnconfirmed:true);
            var manifest=File.ReadAllBytes(job.ManifestPath);var output=File.ReadAllBytes(job.OutputPath);
            var initial=queue.GetPreparedHistoryEntry(job);
            var confirmed=queue.ConfirmHistoryEntry(initial);
            Assert.False(confirmed.Job!.SourceSnapshotUnconfirmed);
            var verified=queue.SimulateHistoryEntry(confirmed);
            Assert.Equal(JobStatus.Simulated,verified.Job!.Status);
            var reopened=SimulationQueue.OpenVersionedV2(path).GetHistoryEntry(job.Id);
            Assert.NotNull(reopened.Job);Assert.Null(reopened.Error);
            Assert.False(reopened.Job!.SourceSnapshotUnconfirmed);Assert.Equal(JobStatus.Simulated,reopened.Job.Status);
            Assert.Equal(manifest,File.ReadAllBytes(job.ManifestPath));Assert.Equal(output,File.ReadAllBytes(job.OutputPath));
        }
        finally { Clean(path); }
    }

    [Theory]
    [InlineData(JobFormat.Dxf)]
    [InlineData(JobFormat.Hpgl)]
    public void ChangedOutputAfterPublicationDoesNotCreateManifestOrDeleteEvidence(JobFormat format)
    {
        if(!OperatingSystem.IsWindows())return;
        var path=NewDirectory();
        try
        {
            var queue=SimulationQueue.OpenVersionedV2(path);string? published=null;byte[]? changed=null;
            var hook=typeof(SimulationQueue).GetProperty("AfterVersionedOutputForVerification",BindingFlags.Instance|BindingFlags.NonPublic)!;
            hook.SetValue(queue,(Action<string>)(output=>
            {
                published=output;
                File.AppendAllText(output,"external change");
                changed=File.ReadAllBytes(output);
            }));
            var failure=Assert.Throws<IOException>(()=>queue.Prepare(Document(),Profile,format));
            Assert.Contains("yayımlanmış olabilir",failure.Message);
            Assert.NotNull(published);Assert.NotNull(changed);
            Assert.Equal(changed,File.ReadAllBytes(published!));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(published!)!,"manifest.json")));
            Assert.True(Directory.Exists(Path.GetDirectoryName(published!)!));
        }
        finally { Clean(path); }
    }

    [Fact]
    public void ChangedReviewedHpglAfterPublicationDoesNotCreateManifest()
    {
        if(!OperatingSystem.IsWindows())return;
        var path=NewDirectory();
        try
        {
            var queue=SimulationQueue.OpenVersionedV2(path);var document=Document();
            var proposal=queue.PrepareHpglReview(document,Profile);
            string? output=null;byte[]? evidence=null;
            var hook=typeof(SimulationQueue).GetProperty("AfterVersionedOutputForVerification",BindingFlags.Instance|BindingFlags.NonPublic)!;
            hook.SetValue(queue,(Action<string>)(p=> { output=p;File.AppendAllText(p,"changed");evidence=File.ReadAllBytes(p); }));
            var failure=Assert.Throws<IOException>(()=>queue.CommitPreparedHpgl(proposal,document,Profile));
            Assert.Contains("yayımlanmış olabilir",failure.Message);
            Assert.NotNull(output);Assert.Equal(evidence,File.ReadAllBytes(output!));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(output!)!,"manifest.json")));
        }
        finally { Clean(path); }
    }

    [Fact]
    public void LaterEventIsNotMistakenForEventWrittenByThisOperation()
    {
        if(!OperatingSystem.IsWindows())return;
        var path=NewDirectory();
        try
        {
            var queue=SimulationQueue.OpenVersionedV2(path);
            var job=queue.Prepare(Document(),Profile,JobFormat.Dxf,sourceSnapshotUnconfirmed:true);
            var original=File.ReadAllBytes(job.ManifestPath);
            var entry=queue.GetPreparedHistoryEntry(job);
            var hook=typeof(SimulationQueue).GetProperty("AfterHistoryCommitForVerification",BindingFlags.Instance|BindingFlags.NonPublic)!;
            hook.SetValue(queue,(Action<string>)(manifest=>
            {
                var history=new ImmutableJobHistory(Path.GetDirectoryName(manifest)!);
                var current=history.Inspect();
                Assert.Equal(ImmutableJobHistory.InspectionStatus.Valid,current.Status);
                // A distinct later record, even with the same payload, has a
                // different identity and cannot be adopted as our commit.
                history.Append(current.HeadHash!,current.Payload!);
            }));
            var failure=Assert.Throws<InvalidOperationException>(()=>queue.ConfirmHistoryEntry(entry));
            Assert.Contains("uygulanmış olabilir",failure.Message);
            Assert.Equal(2,Directory.GetFiles(Path.GetDirectoryName(job.ManifestPath)!,"history-*.json").Length);
            Assert.Equal(original,File.ReadAllBytes(job.ManifestPath));
        }
        finally { Clean(path); }
    }

    [Fact]
    public void LegacyReadOnlyRejectsEveryMutationWithoutChangingFiles()
    {
        if(!OperatingSystem.IsWindows())return;
        var path=NewDirectory();
        try
        {
            var old=new SimulationQueue(path,streamingHistory:true);
            var doc=Document();var job=old.Prepare(doc,Profile,JobFormat.Dxf,sourceSnapshotUnconfirmed:true);
            var proposal=old.PrepareHpglReview(doc,Profile);
            var legacy=SimulationQueue.OpenLegacyReadOnly(path);var entry=legacy.GetHistoryEntry(job.Id);
            Assert.NotNull(entry.Job);var before=Snapshot(path);
            Assert.Throws<InvalidOperationException>(()=>legacy.Prepare(doc,Profile,JobFormat.Dxf));
            Assert.Throws<InvalidOperationException>(()=>legacy.PrepareHpglReview(doc,Profile));
            Assert.Throws<InvalidOperationException>(()=>legacy.CommitPreparedHpgl(proposal,doc,Profile));
            Assert.Throws<InvalidOperationException>(()=>legacy.ConfirmHistoryEntry(entry));
            Assert.Throws<InvalidOperationException>(()=>legacy.SimulateHistoryEntry(entry));
            Assert.Throws<InvalidOperationException>(()=>legacy.ConfirmSourceSnapshot(job.Id));
            Assert.Throws<InvalidOperationException>(()=>legacy.Simulate(job.Id));
            AssertUnchanged(before,path);
        }
        finally { Clean(path); }
    }

    [Fact]
    public void StaleEntryAndChangedBaseCannotMutateVersionsOrExternalBytes()
    {
        if(!OperatingSystem.IsWindows())return;
        var path=NewDirectory();
        try
        {
            var queue=SimulationQueue.OpenVersionedV2(path);var job=queue.Prepare(Document(),Profile,JobFormat.Dxf,sourceSnapshotUnconfirmed:true);
            var stale=queue.GetPreparedHistoryEntry(job);queue.ConfirmHistoryEntry(stale);
            var before=Snapshot(path);
            Assert.Throws<InvalidOperationException>(()=>queue.SimulateHistoryEntry(stale));AssertUnchanged(before,path);
            File.AppendAllText(job.ManifestPath," ");var changed=Snapshot(path);
            var invalid=SimulationQueue.OpenVersionedV2(path).GetHistoryEntry(job.Id);
            Assert.Null(invalid.Job);Assert.NotNull(invalid.Error);
            Assert.Throws<InvalidOperationException>(()=>queue.ConfirmHistoryEntry(stale));AssertUnchanged(changed,path);
        }
        finally { Clean(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrChangedOutputIsNeverReportedAsSuccessfulVerification(bool missing)
    {
        if(!OperatingSystem.IsWindows())return;
        var path=NewDirectory();
        try
        {
            var queue=SimulationQueue.OpenVersionedV2(path);var job=queue.Prepare(Document(),Profile,JobFormat.Dxf);
            if(missing)File.Delete(job.OutputPath);else File.AppendAllText(job.OutputPath,"changed");
            var observed=queue.GetHistoryEntry(job.Id);var result=queue.SimulateHistoryEntry(observed);
            Assert.Equal(JobStatus.Failed,result.Job!.Status);Assert.NotNull(result.Job.Error);
            var reopened=SimulationQueue.OpenVersionedV2(path).GetHistoryEntry(job.Id);
            Assert.Equal(JobStatus.Failed,reopened.Job!.Status);
        }
        finally { Clean(path); }
    }

    [Fact]
    public void TwoQueuesCannotBothCommitFromTheSameSelectedVersion()
    {
        if(!OperatingSystem.IsWindows())return;
        var path=NewDirectory();
        try
        {
            var first=SimulationQueue.OpenVersionedV2(path);var job=first.Prepare(Document(),Profile,JobFormat.Dxf,sourceSnapshotUnconfirmed:true);
            var second=SimulationQueue.OpenVersionedV2(path);
            var left=first.GetHistoryEntry(job.Id);var right=second.GetHistoryEntry(job.Id);
            using var start=new Barrier(2);
            Exception? Run(SimulationQueue queue,SimulationQueue.HistoryEntry entry)
            {
                if(!start.SignalAndWait(TimeSpan.FromSeconds(15)))throw new TimeoutException();
                try { queue.ConfirmHistoryEntry(entry);return null; } catch(Exception ex) { return ex; }
            }
            var a=Task.Run(()=>Run(first,left));var b=Task.Run(()=>Run(second,right));Task.WaitAll(a,b);
            Assert.True((a.Result is null) != (b.Result is null));
            var reopened=SimulationQueue.OpenVersionedV2(path).GetHistoryEntry(job.Id);
            Assert.NotNull(reopened.Job);Assert.False(reopened.Job!.SourceSnapshotUnconfirmed);
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(job.ManifestPath)!,"history-*.json"));
        }
        finally { Clean(path); }
    }

    [Fact]
    public void BranchedHistoryIsRejectedWithoutDeletingEitherBranch()
    {
        if(!OperatingSystem.IsWindows())return;
        var path=NewDirectory();using var barrier=new Barrier(2);
        try
        {
            var queue=SimulationQueue.OpenVersionedV2(path);var job=queue.Prepare(Document(),Profile,JobFormat.Dxf,sourceSnapshotUnconfirmed:true);
            var selected=queue.GetPreparedHistoryEntry(job);var dir=Path.GetDirectoryName(job.ManifestPath)!;
            var left=new ImmutableJobHistory(dir);var right=new ImmutableJobHistory(dir);var head=left.Inspect().HeadHash!;
            var hook=typeof(ImmutableJobHistory).GetProperty("BeforePublishForVerification",BindingFlags.Instance|BindingFlags.NonPublic)!;
            Action wait=()=> { if(!barrier.SignalAndWait(TimeSpan.FromSeconds(15)))throw new TimeoutException(); };
            hook.SetValue(left,wait);hook.SetValue(right,wait);
            var options=new JsonSerializerOptions { Converters={new JsonStringEnumConverter()} };
            var a=Task.Run(()=>left.Append(head,JsonSerializer.SerializeToUtf8Bytes(job with { SourceSnapshotUnconfirmed=false },options)));
            var b=Task.Run(()=>right.Append(head,JsonSerializer.SerializeToUtf8Bytes(job with { Status=JobStatus.Failed,Error="test" },options)));
            Task.WaitAll(a,b);
            // Queue updates use this persistent coordination file even when a
            // stale or conflicted selection is rejected.
            var lockPath=Path.Combine(dir,"manifest.json.lock");
            if(!File.Exists(lockPath))File.WriteAllBytes(lockPath,[]);
            var before=Snapshot(path);
            var reopened=SimulationQueue.OpenVersionedV2(path).GetHistoryEntry(job.Id);
            Assert.Null(reopened.Job);Assert.NotNull(reopened.Error);
            Assert.Throws<InvalidOperationException>(()=>queue.ConfirmHistoryEntry(selected));
            AssertUnchanged(before,path);
            Assert.Equal(2,Directory.GetFiles(dir,"history-*.json").Length);
        }
        finally { Clean(path); }
    }
}
