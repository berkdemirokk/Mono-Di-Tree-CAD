using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class ProjectBackupOwnershipTests
{
    private static string NewRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var path = Path.Combine(root!.FullName, "work", "backup-ownership", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
    [Fact]
    public void ExistingStagingDirectoryIsNeverClaimedOrAltered()
    {
        var path = Path.Combine(NewRoot(), "stage"); Directory.CreateDirectory(path);
        var sentinel = Path.Combine(path, "keep.txt"); File.WriteAllText(sentinel, "owned by someone else");
        var method = typeof(ProjectBackup).GetMethod("CreateOwnedDirectory", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.IsAssignableFrom<IOException>(Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [path])).InnerException);
        Assert.Equal("owned by someone else", File.ReadAllText(sentinel));
    }
    [Fact]
    public void ExistingArchiveIsNotOverwritten()
    {
        var root = NewRoot(); var source = Path.Combine(root, "projects"); Directory.CreateDirectory(source);
        ProjectStorage.Save(new ProjectDocument(), Path.Combine(source, "project.tcad"));
        var destination = Path.Combine(root, "existing.zip"); File.WriteAllText(destination, "existing data");
        Assert.ThrowsAny<IOException>(() => ProjectBackup.Create(source, destination));
        Assert.Equal("existing data", File.ReadAllText(destination));
    }
}
