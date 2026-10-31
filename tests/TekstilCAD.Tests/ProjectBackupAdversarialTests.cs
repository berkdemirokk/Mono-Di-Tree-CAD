using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class ProjectBackupAdversarialTests
{
    private static string NewRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Test repository not found.");
        var path = Path.Combine(root.FullName, "work", "backup-adversarial-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Entry(ZipArchive zip, string name, string text)
    {
        using var stream = zip.CreateEntry(name).Open();
        var bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes);
    }

    [Theory]
    [InlineData("projects/../outside.tcad")]
    [InlineData("/projects/outside.tcad")]
    [InlineData("projects\\outside.tcad")]
    [InlineData("C:/outside.tcad")]
    public void UnsafeEntryNamesCannotCreateDestination(string name)
    {
        var root = NewRoot(); var archive = Path.Combine(root, "bad.zip"); var target = Path.Combine(root, "restore");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) { Entry(zip, name, "x"); Entry(zip, "manifest.json", "{}"); }
        Assert.Throws<InvalidDataException>(() => ProjectBackup.Restore(archive, target));
        Assert.False(Directory.Exists(target)); Assert.False(File.Exists(Path.Combine(root, "outside.tcad")));
    }

    [Theory]
    [InlineData("projects/a.tcad", "projects/a.tcad")]
    [InlineData("projects/a.tcad", "PROJECTS/A.TCAD")]
    public void DuplicateAndCaseCollidingZipEntriesAreRejected(string first, string second)
    {
        var root = NewRoot(); var archive = Path.Combine(root, "duplicate.zip"); var target = Path.Combine(root, "restore");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) { Entry(zip, first, "x"); Entry(zip, second, "y"); Entry(zip, "manifest.json", "{}"); }
        Assert.Throws<InvalidDataException>(() => ProjectBackup.Restore(archive, target));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void SameLengthTamperingFailsHashBeforeRestore()
    {
        var root = NewRoot(); var archive = Path.Combine(root, "tampered.zip"); var target = Path.Combine(root, "restore");
        const string original = "abcd"; const string tampered = "abce";
        var manifest = JsonSerializer.Serialize(new { Version = 1, Files = new[] { new { Name = "a.tcad", Length = original.Length, Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original))) } } });
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) { Entry(zip, "projects/a.tcad", tampered); Entry(zip, "manifest.json", manifest); }
        Assert.Throws<InvalidDataException>(() => ProjectBackup.Restore(archive, target));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void MalformedManifestCannotCreateDestination()
    {
        var root = NewRoot(); var archive = Path.Combine(root, "bad-manifest.zip"); var target = Path.Combine(root, "restore");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) Entry(zip, "manifest.json", "{not-json");
        Assert.ThrowsAny<JsonException>(() => ProjectBackup.Restore(archive, target));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void ExistingDestinationRemainsUntouched()
    {
        var root = NewRoot(); var archive = Path.Combine(root, "bad.zip"); var target = Path.Combine(root, "restore");
        Directory.CreateDirectory(target); var sentinel = Path.Combine(target, "existing.txt"); File.WriteAllText(sentinel, "keep");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) Entry(zip, "manifest.json", "{}");
        Assert.ThrowsAny<IOException>(() => ProjectBackup.Restore(archive, target));
        Assert.Equal("keep", File.ReadAllText(sentinel)); Assert.Single(Directory.GetFiles(target));
    }

    [Fact]
    public void EntryCountLimitRejectsArchiveBeforeDestinationCreation()
    {
        var root = NewRoot(); var archive = Path.Combine(root, "many.zip"); var target = Path.Combine(root, "restore");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            for (var i = 0; i < 1002; i++) zip.CreateEntry($"projects/{i}.tcad");
        Assert.Throws<InvalidDataException>(() => ProjectBackup.Restore(archive, target));
        Assert.False(Directory.Exists(target));
    }
}
