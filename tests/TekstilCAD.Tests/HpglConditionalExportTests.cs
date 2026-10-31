using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class HpglConditionalExportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedOrNewDestinationIsPreserved(bool initiallyExists)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var dir = Path.Combine(root!.FullName, "work", "hpgl-conditional-tests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".plt");
        var project = new ProjectDocument { Pieces = [new PatternPiece { Points = [new(0, 0), new(10, 0), new(0, 10)] }] };
        try
        {
            if (initiallyExists) File.WriteAllText(path, "original");
            var token = ProjectStorage.CaptureVersion(path);
            File.WriteAllText(path, "external edit");
            Assert.Throws<ProjectConflictException>(() => HpglFormat.Export(project, path, 40, token));
            Assert.Equal("external edit", File.ReadAllText(path));
            HpglFormat.Export(project, path, 40, ProjectStorage.CaptureVersion(path));
            Assert.StartsWith("IN;SP1;PA;", File.ReadAllText(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void TokenForAnotherPathCannotAuthorizeExport()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var dir = Path.Combine(root!.FullName, "work", "hpgl-conditional-tests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".plt");
        var other = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".plt");
        var project = new ProjectDocument { Pieces = [new PatternPiece { Points = [new(0, 0), new(10, 0)], Closed = false }] };
        var token = ProjectStorage.CaptureVersion(other);
        Assert.Throws<ArgumentException>(() => HpglFormat.Export(project, path, 40, token));
        Assert.False(File.Exists(path));
    }
}
