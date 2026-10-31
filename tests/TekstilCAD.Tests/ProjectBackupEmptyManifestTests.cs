using System.IO.Compression;
using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class ProjectBackupEmptyManifestTests
{
    [Fact]
    public void EmptyManifestDoesNotCreateRestoreDirectory()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md"))) repository = repository.Parent;
        if (repository is null) throw new InvalidOperationException("Test repository not found.");
        var root = Path.Combine(repository.FullName, "work", "backup-empty-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var archive = Path.Combine(root, "empty.zip");
        var destination = Path.Combine(root, "restore");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        using (var output = zip.CreateEntry("manifest.json").Open())
            output.Write(Encoding.UTF8.GetBytes("{\"Version\":1,\"Files\":[]}"));

        Assert.Throws<InvalidDataException>(() => ProjectBackup.Restore(archive, destination));
        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetDirectories(root, "restore.staging-*"));
    }
}
