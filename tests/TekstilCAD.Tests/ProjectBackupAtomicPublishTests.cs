using System.Reflection;
using System.Runtime.ExceptionServices;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class ProjectBackupAtomicPublishTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LockedSourceCannotBeReplacedAndExistingTargetIsNeverOverwritten(bool createTarget)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md"))) root=root.Parent;
        Assert.NotNull(root);
        var dir=Path.Combine(root!.FullName,"work","backup-atomic-publish",Guid.NewGuid().ToString("N"));
        var source=Path.Combine(dir,"projects"); Directory.CreateDirectory(source);
        ProjectStorage.Save(new ProjectDocument { Name="Original" },Path.Combine(source,"pattern.tcad"));
        var target=Path.Combine(dir,"backup.zip");
        var method=typeof(ProjectBackup).GetMethod("CreateWithSummaryForVerification",BindingFlags.Static|BindingFlags.NonPublic);
        Assert.NotNull(method);
        Action<string> locked = path =>
        {
            Assert.ThrowsAny<IOException>(() => File.Move(path,path+".displaced"));
            Assert.ThrowsAny<IOException>(() => File.WriteAllText(path,"foreign"));
            if(createTarget) File.WriteAllText(target,"existing");
        };
        void Invoke()
        {
            try { method!.Invoke(null,[source,target,null,locked]); }
            catch(TargetInvocationException ex) when(ex.InnerException is not null)
            { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
        }
        if(createTarget)
        {
            Assert.ThrowsAny<IOException>(Invoke);
            Assert.Equal("existing",File.ReadAllText(target));
        }
        else
        {
            Invoke();
            Assert.True(File.Exists(target));
            using var archive = System.IO.Compression.ZipFile.OpenRead(target);
            Assert.Contains(archive.Entries, entry => entry.FullName == "manifest.json");
        }
    }
}
