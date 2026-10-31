using System.Reflection;
using System.Runtime.ExceptionServices;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class ProjectBackupRestoreHandleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VerifiedDirectoryHandleBlocksReplacementAndNeverReplacesTarget(bool createTarget)
    {
        if (!OperatingSystem.IsWindows()) return;
        var ancestor = new DirectoryInfo(AppContext.BaseDirectory);
        while (ancestor is not null && !File.Exists(Path.Combine(ancestor.FullName, "AGENTS.md"))) ancestor = ancestor.Parent;
        Assert.NotNull(ancestor);
        var root = Path.Combine(ancestor!.FullName, "work", "restore-handle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var stage = Path.Combine(root, "stage"); var target = Path.Combine(root, "target");
        Directory.CreateDirectory(stage); File.WriteAllText(Path.Combine(stage, "owned.txt"), "owned");
        var stamp = typeof(FileIdentity).GetMethod("CaptureOwnedDirectory", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [stage]);
        var atomic = typeof(ProjectBackup).Assembly.GetType("TekstilCAD.FileFormats.ProjectBackupAtomic")!;
        var publish = atomic.GetMethod("PublishDirectory", BindingFlags.Static | BindingFlags.NonPublic)!;
        Action verify = () =>
        {
            Assert.ThrowsAny<IOException>(() => Directory.Move(stage, stage + ".moved"));
            if (createTarget) { Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "foreign.txt"), "foreign"); }
        };
        void Run()
        {
            try { publish.Invoke(null, [stage, target, stamp, verify]); }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
        }
        if (createTarget)
        {
            Assert.ThrowsAny<IOException>(Run);
            Assert.Equal("foreign", File.ReadAllText(Path.Combine(target, "foreign.txt")));
            Assert.True(File.Exists(Path.Combine(stage, "owned.txt")));
        }
        else
        {
            Run();
            Assert.False(Directory.Exists(stage));
            Assert.Equal("owned", File.ReadAllText(Path.Combine(target, "owned.txt")));
        }
    }
}
