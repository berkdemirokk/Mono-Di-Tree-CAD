using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class ProjectBackupUnsupportedPathTests
{
    [Fact]
    public void ExtendedPathIsRejectedBeforeStagingIsCreated()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var dir = Path.Combine(root!.FullName, "work", "backup-unsupported-path", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(dir, "source");
        Directory.CreateDirectory(source);
        ProjectStorage.Save(new ProjectDocument(), Path.Combine(source, "a.tcad"));
        var target = Path.Combine(dir, "backup.zip");
        Assert.Throws<InvalidOperationException>(() => ProjectBackup.Create(source, @"\\?\" + target));
        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFiles(dir, "backup.zip.staging-*"));
    }
}
