using System.Runtime.InteropServices;
using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public sealed class HardLinkRecoveryQueueProtectionTests
{
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newFileName, string existingFileName, IntPtr securityAttributes);

    private static string TestDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root missing.");
        var path = Path.Combine(root.FullName, "work", "hardlink-recovery-queue-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }

    private static ProjectDocument Document() => new() { Name="Hardlink test", Pieces=[new PatternPiece
    { Name="Part", Points=[new(0,0),new(10,0),new(10,10),new(0,10)] }] };

    [Fact]
    public void ClearOwnRefusesMultiplyLinkedRecoveryWithoutDeletingEitherName()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory=TestDirectory();
        using var store=new RecoveryStore(directory);
        store.Save(Document(), []);
        var alias=Path.Combine(directory,"recovery-alias.tcr");
        Assert.True(CreateHardLink(alias,store.DraftRecoveryPath,IntPtr.Zero));
        var before=File.ReadAllBytes(alias);
        Assert.ThrowsAny<IOException>(()=>store.ClearOwn());
        Assert.Equal(before,File.ReadAllBytes(store.DraftRecoveryPath));
        Assert.Equal(before,File.ReadAllBytes(alias));
        Assert.True(FileIdentity.SameExistingFile(alias,store.DraftRecoveryPath));
    }

    [Fact]
    public void ArchiveRefusesMultiplyLinkedRecoveryAndPreservesBothNames()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory=TestDirectory(); string source;
        using (var writer=new RecoveryStore(directory)) { writer.Save(Document(),[]); source=writer.DraftRecoveryPath; }
        using var reader=new RecoveryStore(directory);
        var recovered=reader.LoadCandidate(source);
        var alias=Path.Combine(directory,"archive-alias.tcr");
        Assert.True(CreateHardLink(alias,source,IntPtr.Zero));
        var before=File.ReadAllBytes(source);
        Assert.ThrowsAny<IOException>(()=>reader.ArchiveRecovered(recovered));
        Assert.Equal(before,File.ReadAllBytes(source));
        Assert.Equal(before,File.ReadAllBytes(alias));
        Assert.True(FileIdentity.SameExistingFile(source,alias));
    }

    [Fact]
    public void QueueManifestReplacementRefusesMultiplyLinkedTarget()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory=TestDirectory();
        var queue=new SimulationQueue(directory);
        var job=queue.Prepare(Document(),new MachineProfile("Local"),JobFormat.Dxf,sourceSnapshotUnconfirmed:true);
        var alias=Path.Combine(directory,"manifest-alias.json");
        Assert.True(CreateHardLink(alias,job.ManifestPath,IntPtr.Zero));
        var before=File.ReadAllBytes(job.ManifestPath);
        Assert.ThrowsAny<IOException>(()=>queue.ConfirmSourceSnapshot(job.Id));
        Assert.Equal(before,File.ReadAllBytes(job.ManifestPath));
        Assert.Equal(before,File.ReadAllBytes(alias));
        Assert.True(FileIdentity.SameExistingFile(job.ManifestPath,alias));
        Assert.True(Assert.Single(queue.Jobs).SourceSnapshotUnconfirmed);
    }

    [Fact]
    public void RecoveryCandidateLeaseRefusesMultiplyLinkedLock()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory=TestDirectory(); string source;
        using (var writer=new RecoveryStore(directory)) { writer.Save(Document(),[]); source=writer.DraftRecoveryPath; }
        var lockPath=Path.ChangeExtension(source,".lock");
        var alias=Path.Combine(directory,"lock-alias");
        Assert.True(CreateHardLink(alias,lockPath,IntPtr.Zero));
        var before=File.ReadAllBytes(lockPath);
        using var reader=new RecoveryStore(directory);
        Assert.ThrowsAny<IOException>(()=>reader.LoadCandidate(source));
        Assert.Equal(before,File.ReadAllBytes(lockPath));
        Assert.True(FileIdentity.SameExistingFile(lockPath,alias));
    }

    [Fact]
    public void QueueRefusesMultiplyLinkedManifestLock()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory=TestDirectory();
        var queue=new SimulationQueue(directory);
        var job=queue.Prepare(Document(),new MachineProfile("Local"),JobFormat.Dxf,sourceSnapshotUnconfirmed:true);
        var lockPath=job.ManifestPath+".lock";
        var alias=Path.Combine(directory,"manifest-lock-alias");
        Assert.True(CreateHardLink(alias,lockPath,IntPtr.Zero));
        var before=File.ReadAllBytes(job.ManifestPath);
        Assert.ThrowsAny<IOException>(()=>queue.ConfirmSourceSnapshot(job.Id));
        Assert.Equal(before,File.ReadAllBytes(job.ManifestPath));
        Assert.True(FileIdentity.SameExistingFile(lockPath,alias));
        Assert.True(Assert.Single(queue.Jobs).SourceSnapshotUnconfirmed);
    }

    [Fact]
    public void QueueDoesNotPublishOrDeleteReplacedTemporaryFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory=TestDirectory();
        var queue=new SimulationQueue(directory);
        var job=queue.Prepare(Document(),new MachineProfile("Local"),JobFormat.Dxf,sourceSnapshotUnconfirmed:true);
        var before=File.ReadAllBytes(job.ManifestPath);
        string? temporary=null;
        var hook=typeof(SimulationQueue).GetProperty("BeforeManifestCommitForVerification",BindingFlags.Instance|BindingFlags.NonPublic);
        Assert.NotNull(hook);
        hook!.SetValue(queue,(Action<string>)(path=>
        {
            temporary=path;
            File.Move(path,Path.Combine(directory,"owned-queue-temp"));
            File.WriteAllText(path,"foreign temp");
        }));
        Assert.ThrowsAny<IOException>(()=>queue.ConfirmSourceSnapshot(job.Id));
        Assert.Equal(before,File.ReadAllBytes(job.ManifestPath));
        Assert.Equal("foreign temp",File.ReadAllText(temporary!));
        Assert.True(File.Exists(Path.Combine(directory,"owned-queue-temp")));
        Assert.True(Assert.Single(queue.Jobs).SourceSnapshotUnconfirmed);
    }
}
