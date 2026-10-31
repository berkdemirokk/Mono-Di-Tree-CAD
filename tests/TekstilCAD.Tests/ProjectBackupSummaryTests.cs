using TekstilCAD.Core;
using TekstilCAD.FileFormats;
namespace TekstilCAD.Tests;

public sealed class ProjectBackupSummaryTests
{
    [Fact]
    public void SummaryReportsOnlyPublishedTopLevelProjectsAndActualBytes()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var work = Path.Combine(root!.FullName, "work", "backup-summary-tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(work, "source"); var nested = Path.Combine(source, "nested");
        Directory.CreateDirectory(nested);
        var a = Path.Combine(source, "a.tcad"); var b = Path.Combine(source, "b.tcad");
        ProjectStorage.Save(new ProjectDocument { Name = "A" }, a);
        ProjectStorage.Save(new ProjectDocument { Name = "B" }, b);
        ProjectStorage.Save(new ProjectDocument { Name = "Excluded" }, Path.Combine(nested, "nested.tcad"));
        File.WriteAllText(Path.Combine(source, "notes.txt"), "excluded");
        var archive = Path.Combine(work, "backup.zip");
        var created = ProjectBackup.CreateWithSummary(source, archive);
        Assert.Equal(2, created.FileCount);
        Assert.Equal(new[] { "a.tcad", "b.tcad" }, created.FileNames);
        Assert.Equal(new FileInfo(a).Length + new FileInfo(b).Length, created.TotalBytes);
        Assert.Contains("alt klasörler", ProjectBackupSummary.Scope);
        var target = Path.Combine(work, "restored");
        var restored = ProjectBackup.RestoreWithSummary(archive, target);
        Assert.Equal(created.FileCount, restored.FileCount);
        Assert.Equal(created.TotalBytes, restored.TotalBytes);
        Assert.Equal(created.FileNames, restored.FileNames);
        Assert.False(Directory.Exists(Path.Combine(target, "nested")));
        Assert.False(File.Exists(Path.Combine(target, "notes.txt")));
        Assert.Throws<IOException>(() => ProjectBackup.RestoreWithSummary(archive, target));
    }
}
