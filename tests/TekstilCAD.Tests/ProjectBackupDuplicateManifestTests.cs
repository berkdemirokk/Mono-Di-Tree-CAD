using System.IO.Compression;
using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class ProjectBackupDuplicateManifestTests
{
    [Theory]
    [InlineData("\"Version\":1", "\"Version\":2,\"Version\":1")]
    [InlineData("\"Name\":\"a.tcad\"", "\"Name\":\"other.tcad\",\"Name\":\"a.tcad\"")]
    public void ConflictingPropertiesAreRejectedBeforeStaging(string original, string replacement)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md"))) repository = repository.Parent;
        if (repository is null) throw new InvalidOperationException("Test repository not found.");
        var root = Path.Combine(repository.FullName, "work", "backup-duplicate-manifest-tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source"); Directory.CreateDirectory(source);
        ProjectStorage.Save(new ProjectDocument(), Path.Combine(source, "a.tcad"));
        var archive = Path.Combine(root, "backup.zip"); ProjectBackup.Create(source, archive);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("manifest.json")!;
            string json;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) json = reader.ReadToEnd();
            Assert.Contains(original, json);
            entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open(), new UTF8Encoding(false));
            writer.Write(json.Replace(original, replacement, StringComparison.Ordinal));
        }

        var destination = Path.Combine(root, "restored");
        Assert.Throws<InvalidDataException>(() => ProjectBackup.Restore(archive, destination));
        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetDirectories(root, "restored.staging-*"));
    }
}
