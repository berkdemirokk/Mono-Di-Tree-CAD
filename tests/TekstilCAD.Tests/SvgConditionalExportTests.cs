using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class SvgConditionalExportTests
{
    private static string WorkDir()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root not found.");
        var path = Path.Combine(root.FullName, "work", "svg-conditional-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
    private static ProjectDocument Sample() => new() { Pieces = [new() { Points = [new(0,0), new(10,0), new(0,10)] }] };

    [Fact]
    public void ChangedTargetIsPreservedAndUnchangedTargetCanBeExported()
    {
        var dir = WorkDir(); var path = Path.Combine(dir, "drawing.svg");
        File.WriteAllText(path, "original"); var stale = ProjectStorage.CaptureVersion(path);
        File.WriteAllText(path, "other writer");
        Assert.Throws<ProjectConflictException>(() => SvgFormat.Export(Sample(), path, stale));
        Assert.Equal("other writer", File.ReadAllText(path));
        var current = ProjectStorage.CaptureVersion(path);
        SvgFormat.Export(Sample(), path, current);
        Assert.Contains("<svg", File.ReadAllText(path));
    }

    [Fact]
    public void MissingTargetCreatedAfterCaptureIsNotOverwritten()
    {
        var path = Path.Combine(WorkDir(), "drawing.svg");
        var missing = ProjectStorage.CaptureVersion(path);
        File.WriteAllText(path, "new file");
        Assert.Throws<ProjectConflictException>(() => SvgFormat.Export(Sample(), path, missing));
        Assert.Equal("new file", File.ReadAllText(path));
    }
}
