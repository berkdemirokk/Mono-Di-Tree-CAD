using System.Security.Cryptography;
using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class StorageConcurrencyTests : IDisposable
{
    private readonly string directory;
    private readonly string testRoot;
    public StorageConcurrencyTests()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        testRoot=Path.GetFullPath(Path.Combine(root.FullName,"work","storage-concurrency-tests"));
        directory = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }
    private string Target => Path.Combine(directory, "project.tcad");
    private static ProjectDocument Document(string name = "Temel") => new()
    {
        Name = name,
        Pieces = [new() { Points = [new(0, 0), new(100, 0), new(100, 100)], Name = "Ön beden" }]
    };
    [Fact]
    public void SecondSessionCannotOverwriteFirstSessionsNewContent()
    {
        ProjectStorage.Save(Document(), Target);
        var first = ProjectStorage.LoadWithVersion(Target);
        var second = ProjectStorage.LoadWithVersion(Target);
        first.Project.Name = "Birinci kayıt";
        var next = ProjectStorage.SaveIfUnchanged(first.Project, Target, first.Version);
        second.Project.Name = "İkinci kayıt";
        Assert.Throws<ProjectConflictException>(() => ProjectStorage.SaveIfUnchanged(second.Project, Target, second.Version));
        Assert.Equal("Birinci kayıt", ProjectStorage.Load(Target).Name);
        Assert.Equal("İkinci kayıt", second.Project.Name);
        Assert.Equal(next, ProjectStorage.CaptureVersion(Target));
    }
    [Fact]
    public void FirstCreationRejectsAFileCreatedSinceMissingToken()
    {
        var missing = ProjectStorage.CaptureVersion(Target);
        Assert.Null(missing.Sha256);
        ProjectStorage.SaveIfUnchanged(Document("A"), Target, missing);
        Assert.Throws<ProjectConflictException>(() => ProjectStorage.SaveIfUnchanged(Document("B"), Target, missing));
        Assert.Equal("A", ProjectStorage.Load(Target).Name);
    }
    [Fact]
    public void ExternalChangeIsDetectedEvenWhenLengthAndTimestampAreUnchanged()
    {
        ProjectStorage.Save(Document("AAA"), Target);
        var loaded = ProjectStorage.LoadWithVersion(Target);
        var time = File.GetLastWriteTimeUtc(Target);
        var beforeLength = new FileInfo(Target).Length;
        File.WriteAllText(Target, File.ReadAllText(Target).Replace("AAA", "BBB"));
        File.SetLastWriteTimeUtc(Target, time);
        Assert.Equal(beforeLength, new FileInfo(Target).Length);
        Assert.Throws<ProjectConflictException>(() => ProjectStorage.SaveIfUnchanged(loaded.Project, Target, loaded.Version));
        Assert.Equal("BBB", ProjectStorage.Load(Target).Name);
    }
    [Fact]
    public void DeletedFileIsAConflictRatherThanSilentRecreation()
    {
        ProjectStorage.Save(Document(), Target);
        var loaded = ProjectStorage.LoadWithVersion(Target);
        File.Delete(Target);
        Assert.Throws<ProjectConflictException>(() => ProjectStorage.SaveIfUnchanged(loaded.Project, Target, loaded.Version));
        Assert.False(File.Exists(Target));
    }
    [Fact]
    public void TokenCannotBeReusedForAnotherPath()
    {
        var token = ProjectStorage.CaptureVersion(Target);
        var other = Path.Combine(directory, "other.tcad");
        Assert.Throws<ArgumentException>(() => ProjectStorage.SaveIfUnchanged(Document(), other, token));
        Assert.False(File.Exists(other));
    }
    [Fact]
    public void InvalidDocumentLeavesPreviousVersionAndBytesUntouched()
    {
        ProjectStorage.Save(Document(), Target);
        var loaded = ProjectStorage.LoadWithVersion(Target);
        var bytes = File.ReadAllBytes(Target);
        loaded.Project.Pieces[0].Points[0] = new(double.NaN, 0);
        Assert.Throws<InvalidDataException>(() => ProjectStorage.SaveIfUnchanged(loaded.Project, Target, loaded.Version));
        Assert.Equal(bytes, File.ReadAllBytes(Target));
        Assert.Equal(loaded.Version, ProjectStorage.CaptureVersion(Target));
        Assert.Single(Directory.GetFiles(directory));
    }
    [Fact]
    public void LoadTokenHashesTheExactBomAwareSnapshot()
    {
        ProjectStorage.Save(Document(), Target);
        var content = File.ReadAllText(Target);
        File.WriteAllText(Target, content, new UTF8Encoding(true));
        var loaded = ProjectStorage.LoadWithVersion(Target);
        Assert.Equal("Temel", loaded.Project.Name);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Target))), loaded.Version.Sha256);
        loaded.Project.Name = "Güncel";
        var token = ProjectStorage.SaveIfUnchanged(loaded.Project, Target, loaded.Version);
        Assert.Equal(ProjectStorage.CaptureVersion(Target), token);
    }
    [Fact]
    public async Task CooperativeThreadsSerializeCheckAndReplace()
    {
        ProjectStorage.Save(Document(), Target);
        var token = ProjectStorage.CaptureVersion(Target);
        using var start = new ManualResetEventSlim(false);
        var attempts = Enumerable.Range(0, 2).Select(i => Task.Run(() =>
        {
            start.Wait();
            try { ProjectStorage.SaveIfUnchanged(Document("Oturum " + i), Target, token); return true; }
            catch (ProjectConflictException) { return false; }
        })).ToArray();
        start.Set();
        var outcomes=await Task.WhenAll(attempts);
        Assert.Single(outcomes,success=>success);
        Assert.Single(outcomes,success=>!success);
        Assert.StartsWith("Oturum ", ProjectStorage.Load(Target).Name);
        Assert.Single(Directory.GetFiles(directory));
    }
    [Fact]
    public void NormalizedDotSegmentPathSharesVersionAndWriteContract()
    {
        ProjectStorage.Save(Document(), Target);
        var alias = Path.Combine(directory, ".", "project.tcad");
        var loaded = ProjectStorage.LoadWithVersion(alias);
        Assert.Equal(Path.GetFullPath(Target), loaded.Version.FullPath);
        loaded.Project.Name = "Normalize";
        ProjectStorage.SaveIfUnchanged(loaded.Project, Target, loaded.Version);
        Assert.Equal("Normalize", ProjectStorage.Load(alias).Name);
    }
    public void Dispose()
    {
        var resolved=Path.GetFullPath(directory);
        Assert.Equal(testRoot,Path.GetDirectoryName(resolved));
        Directory.Delete(resolved,true);
    }
}
