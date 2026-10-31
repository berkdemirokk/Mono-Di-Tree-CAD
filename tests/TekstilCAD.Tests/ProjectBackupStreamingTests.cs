using System.Security.Cryptography;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class ProjectBackupStreamingTests
{
    private static string Root()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root not found.");
        var path = Path.Combine(root.FullName, "work", "backup-stream-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void MultipleProjectsLargerThanStreamBufferRoundTripByteForByte()
    {
        var root = Root(); var source = Path.Combine(root, "source"); Directory.CreateDirectory(source);
        for (var i = 0; i < 3; i++)
        {
            var piece = new PatternPiece { Name = "Parça " + i, Points = Enumerable.Range(0, 3000).Select(n => new Point2(n, n % 31)).ToList(), Closed = false };
            ProjectStorage.Save(new ProjectDocument { Name = "Model " + i, Pieces = [piece] }, Path.Combine(source, $"{i}.tcad"));
        }
        var original = Directory.GetFiles(source, "*.tcad").ToDictionary(p => Path.GetFileName(p)!, p => File.ReadAllBytes(p), StringComparer.OrdinalIgnoreCase);
        Assert.All(original.Values, bytes => Assert.True(bytes.Length > 81920));
        var zip = Path.Combine(root, "projects.zip"); var summary = ProjectBackup.CreateWithSummary(source, zip);
        var target = Path.Combine(root, "restored"); var restored = ProjectBackup.RestoreWithSummary(zip, target);
        Assert.Equal(3, summary.FileCount); Assert.Equal(summary.TotalBytes, restored.TotalBytes);
        foreach (var entry in original)
        {
            var bytes = File.ReadAllBytes(Path.Combine(target, entry.Key!));
            Assert.Equal(SHA256.HashData(entry.Value), SHA256.HashData(bytes));
            Assert.Equal(entry.Value, bytes);
            ProjectStorage.Load(Path.Combine(target, entry.Key!));
        }
    }

    [Fact]
    public void OversizeProjectIsRejectedWithoutPublishingArchive()
    {
        var root = Root(); var source = Path.Combine(root, "source"); Directory.CreateDirectory(source);
        var file = Path.Combine(source, "too-large.tcad");
        using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.SetLength(64L * 1024 * 1024 + 1);
        var zip = Path.Combine(root, "projects.zip");
        Assert.Throws<InvalidDataException>(() => ProjectBackup.Create(source, zip));
        Assert.False(File.Exists(zip)); Assert.Equal(64L * 1024 * 1024 + 1, new FileInfo(file).Length);
    }

    [Fact]
    public void InvalidLaterProjectDoesNotPublishArchiveOrChangeSourceFiles()
    {
        var root = Root(); var source = Path.Combine(root, "source"); Directory.CreateDirectory(source);
        var first = Path.Combine(source, "a.tcad"); var second = Path.Combine(source, "z.tcad");
        ProjectStorage.Save(new ProjectDocument { Name = "Valid" }, first); File.WriteAllText(second, "{broken-json");
        var beforeFirst = File.ReadAllBytes(first); var beforeSecond = File.ReadAllBytes(second);
        var zip = Path.Combine(root, "projects.zip");
        Assert.Throws<InvalidDataException>(() => ProjectBackup.Create(source, zip));
        Assert.False(File.Exists(zip)); Assert.Equal(beforeFirst, File.ReadAllBytes(first)); Assert.Equal(beforeSecond, File.ReadAllBytes(second));
    }
}
