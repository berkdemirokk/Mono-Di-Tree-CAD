using System.Reflection;
using System.Runtime.ExceptionServices;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class ProjectBackupStagingContentTests
{
    [Fact]
    public void InPlaceStagingMutationIsNotPublished()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root!.FullName, "work", "backup-staging-content", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(directory, "projects");
        Directory.CreateDirectory(source);
        var project = Path.Combine(source, "pattern.tcad");
        ProjectStorage.Save(new ProjectDocument { Name = "Original" }, project);
        var original = File.ReadAllBytes(project);
        var target = Path.Combine(directory, "backup.zip");
        string? stage = null;
        Action<string> mutate = path =>
        {
            stage = path;
            using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            int first = file.ReadByte();
            Assert.True(first >= 0);
            file.Position = 0;
            file.WriteByte((byte)(first ^ 0xff));
        };
        var method = typeof(ProjectBackup).GetMethod("CreateWithSummaryCore", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.ThrowsAny<IOException>(() =>
        {
            try { method!.Invoke(null, [source, target, mutate]); }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
        });
        Assert.False(File.Exists(target));
        Assert.NotNull(stage);
        Assert.True(File.Exists(stage));
        Assert.Equal(original, File.ReadAllBytes(project));
    }
}
