using TekstilCAD.Core;
using TekstilCAD.FileFormats;
namespace TekstilCAD.Tests;
public sealed class RecoveryClearOwnershipTests
{
    private static string DirectoryForTest()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        if(root is null)throw new InvalidOperationException("Repository root missing.");
        return Path.Combine(root.FullName,"work","recovery-clear-ownership",Guid.NewGuid().ToString("N"));
    }
    private static ProjectDocument Doc(string name)=>new(){Name=name};
    [Fact] public void ReplacedFileAndModifiedFileArePreserved()
    {
        if(!OperatingSystem.IsWindows())return;
        using var store=new RecoveryStore(DirectoryForTest());store.Save(Doc("bir"));
        var path=store.RecoveryPath;var replacement=path+".new";
        File.Copy(path,replacement);File.Move(replacement,path,true);
        Assert.Throws<IOException>(()=>store.ClearOwn());Assert.True(File.Exists(path));
        using var modifiedStore=new RecoveryStore(DirectoryForTest());modifiedStore.Save(Doc("iki"));
        var modifiedPath=modifiedStore.RecoveryPath;File.AppendAllText(modifiedPath," ");var changed=File.ReadAllBytes(modifiedPath);
        Assert.Throws<IOException>(()=>modifiedStore.ClearOwn());Assert.Equal(changed,File.ReadAllBytes(modifiedPath));
    }
    [Fact] public void LockedClearRejectsCompetingMutationsAndDeletesOnlyOwnedSnapshot()
    {
        if(!OperatingSystem.IsWindows())return;
        using var store=new RecoveryStore(DirectoryForTest());store.Save(Doc("bir"));store.Save(Doc("iki"));var called=false;
        Assert.True(store.ClearOwn(path=>{
            called=true;
            Assert.True(Record.Exception(()=>File.AppendAllText(path,"x")) is IOException or UnauthorizedAccessException);
            Assert.True(Record.Exception(()=>File.Move(path,path+".moved")) is IOException or UnauthorizedAccessException);
            Assert.True(Record.Exception(()=>File.Delete(path)) is IOException or UnauthorizedAccessException);
        }));
        Assert.True(called);Assert.False(File.Exists(store.RecoveryPath));Assert.False(store.ClearOwn());
    }
    [Fact] public void FailureBeforeDeletePreservesOwnedSnapshot()
    {
        if(!OperatingSystem.IsWindows())return;
        using var store=new RecoveryStore(DirectoryForTest());store.Save(Doc("bir"));var bytes=File.ReadAllBytes(store.RecoveryPath);
        Assert.Throws<InvalidOperationException>(()=>store.ClearOwn(_=>throw new InvalidOperationException("injected")));
        Assert.Equal(bytes,File.ReadAllBytes(store.RecoveryPath));Assert.True(store.ClearOwn());
    }
    [Fact] public void RecoverySaveRefusesSnapshotReplacedBeforeNextSave()
    {
        if(!OperatingSystem.IsWindows())return;
        using var store=new RecoveryStore(DirectoryForTest());store.Save(Doc("ilk"));
        File.WriteAllText(store.RecoveryPath,"harici yeni kayıt");var external=File.ReadAllBytes(store.RecoveryPath);
        Assert.Throws<IOException>(()=>store.Save(Doc("ikinci")));
        Assert.Equal(external,File.ReadAllBytes(store.RecoveryPath));
    }
    [Fact] public void RecoverySaveRechecksSnapshotImmediatelyBeforeCommit()
    {
        if(!OperatingSystem.IsWindows())return;
        RecoveryStore? store=null;var inject=false;
        try
        {
            store=new RecoveryStore(DirectoryForTest(),()=>{if(inject)File.WriteAllText(store!.DraftRecoveryPath,"harici kayıt");});
            store.Save(Doc("ilk"),[]);inject=true;
            Assert.Throws<IOException>(()=>store.Save(Doc("ikinci"),[]));
            Assert.Equal("harici kayıt",File.ReadAllText(store.DraftRecoveryPath));
        }
        finally { store?.Dispose(); }
    }
    [Fact] public void FirstRecoverySaveNeverOverwritesFileCreatedAtCommit()
    {
        if(!OperatingSystem.IsWindows())return;
        RecoveryStore? store=null;
        try
        {
            store=new RecoveryStore(DirectoryForTest(),()=>File.WriteAllText(store!.DraftRecoveryPath,"harici kayıt"));
            Assert.Throws<IOException>(()=>store.Save(Doc("ilk"),[]));
            Assert.Equal("harici kayıt",File.ReadAllText(store.DraftRecoveryPath));
        }
        finally { store?.Dispose(); }
    }
}
