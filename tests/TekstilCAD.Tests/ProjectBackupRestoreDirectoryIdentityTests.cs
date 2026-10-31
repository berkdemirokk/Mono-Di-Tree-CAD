using System.Reflection;
using System.Runtime.ExceptionServices;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class ProjectBackupRestoreDirectoryIdentityTests
{
    private static string NewRoot()
    {
        var ancestor = new DirectoryInfo(AppContext.BaseDirectory);
        while (ancestor is not null && !File.Exists(Path.Combine(ancestor.FullName, "AGENTS.md"))) ancestor = ancestor.Parent;
        Assert.NotNull(ancestor);
        var root = Path.Combine(ancestor!.FullName, "work", "restore-directory-identity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static ProjectBackupSummary RestoreAtBoundary(string archive, string destination, Action<string> callback)
    {
        var method = typeof(ProjectBackup).GetMethod("RestoreWithSummaryCore", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        try { return (ProjectBackupSummary)method!.Invoke(null, [archive, destination, callback])!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    [Fact]
    public void ReplacedStageIsNotPublishedOrDeleted()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = NewRoot(); var source = Path.Combine(root, "source"); Directory.CreateDirectory(source);
        ProjectStorage.Save(new ProjectDocument { Name = "Source" }, Path.Combine(source, "a.tcad"));
        var archive = Path.Combine(root, "backup.zip"); ProjectBackup.Create(source, archive);
        var target = Path.Combine(root, "restored"); string? stage = null, displaced = null;
        Assert.ThrowsAny<IOException>(() => RestoreAtBoundary(archive, target, path =>
        {
            stage = path; displaced = path + ".original"; Directory.Move(path, displaced);
            Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "foreign.txt"), "foreign");
        }));
        Assert.False(Directory.Exists(target));
        Assert.Equal("foreign", File.ReadAllText(Path.Combine(stage!, "foreign.txt")));
        Assert.True(File.Exists(Path.Combine(displaced!, "a.tcad")));
        Assert.Equal("Source", ProjectStorage.Load(Path.Combine(source, "a.tcad")).Name);
    }

    [Fact]
    public void UnchangedStageRestoresNormally()
    {
        var root = NewRoot(); var source = Path.Combine(root, "source"); Directory.CreateDirectory(source);
        ProjectStorage.Save(new ProjectDocument { Name = "Source" }, Path.Combine(source, "a.tcad"));
        var archive = Path.Combine(root, "backup.zip"); ProjectBackup.Create(source, archive);
        var target = Path.Combine(root, "restored"); var summary = ProjectBackup.RestoreWithSummary(archive, target);
        Assert.Equal(1, summary.FileCount);
        Assert.Equal("Source", ProjectStorage.Load(Path.Combine(target, "a.tcad")).Name);
    }

    [Theory]
    [InlineData("modify")]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("directory")]
    public void ChangedStageContentsAreNotPublishedOrDeleted(string change)
    {
        var root = NewRoot(); var source = Path.Combine(root, "source"); Directory.CreateDirectory(source);
        var original = Path.Combine(source, "a.tcad"); ProjectStorage.Save(new ProjectDocument { Name = "Source" }, original);
        var before = File.ReadAllBytes(original);
        var archive = Path.Combine(root, "backup.zip"); ProjectBackup.Create(source, archive);
        var target = Path.Combine(root, "restored"); string? stage = null;
        Assert.ThrowsAny<IOException>(() => RestoreAtBoundary(archive, target, path =>
        {
            stage = path; var staged = Path.Combine(path, "a.tcad");
            switch (change)
            {
                case "modify": var bytes = File.ReadAllBytes(staged); bytes[bytes.Length / 2] ^= 1; File.WriteAllBytes(staged, bytes); break;
                case "missing": File.Delete(staged); break;
                case "extra": File.WriteAllText(Path.Combine(path, "unexpected.txt"), "foreign"); break;
                case "directory": Directory.CreateDirectory(Path.Combine(path, "foreign-directory")); break;
            }
        }));
        Assert.False(Directory.Exists(target));
        Assert.True(Directory.Exists(stage));
        Assert.Equal(before, File.ReadAllBytes(original));
        if (change == "extra") Assert.Equal("foreign", File.ReadAllText(Path.Combine(stage!, "unexpected.txt")));
        if (change == "directory") Assert.True(Directory.Exists(Path.Combine(stage!, "foreign-directory")));
    }
}
