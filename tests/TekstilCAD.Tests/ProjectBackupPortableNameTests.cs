using System.IO.Compression;
using System.Text.Json;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class ProjectBackupPortableNameTests
{
    [Theory]
    [InlineData("a<b.tcad")]
    [InlineData("a>b.tcad")]
    [InlineData("a\"b.tcad")]
    [InlineData("a|b.tcad")]
    [InlineData("a?b.tcad")]
    [InlineData("a*b.tcad")]
    [InlineData("a\u0001b.tcad")]
    [InlineData("COM¹.tcad")]
    [InlineData("lpt³.other.tcad")]
    public void RestoreRejectsNamesThatAreNotPortableToWindows(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var testRoot = Path.Combine(root!.FullName, "work", "backup-portable-name-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        var archive = Path.Combine(testRoot, "backup.zip");
        var destination = Path.Combine(testRoot, "restored");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            zip.CreateEntry("projects/" + name);
            using var manifest = zip.CreateEntry("manifest.json").Open();
            JsonSerializer.Serialize(manifest, new { Version = 1, Files = new[] { new { Name = name, Length = 0, Sha256 = "" } } });
        }

        Assert.Throws<InvalidDataException>(() => ProjectBackup.Restore(archive, destination));
        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetDirectories(testRoot, "restored.staging-*"));
    }
}
