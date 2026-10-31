using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class PlotExportCompletenessTests
{
    private static string Target()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root not found.");
        var dir = Path.Combine(root.FullName, "work", "plot-completeness-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "existing.out");
        File.WriteAllText(path, "existing");
        return path;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void IncompletePieceFailsBeforeSvgOrHpglOverwritesTarget(int count)
    {
        var project = new ProjectDocument { Pieces = [new() { Points = [.. Enumerable.Repeat(new Point2(0, 0), count)] }] };
        var path = Target();
        Assert.Throws<InvalidDataException>(() => SvgFormat.Export(project, path));
        Assert.Equal("existing", File.ReadAllText(path));
        Assert.Throws<InvalidDataException>(() => HpglFormat.Export(project, path));
        Assert.Equal("existing", File.ReadAllText(path));
    }

    [Fact]
    public void InvalidXmlNameFailsBeforeSvgOverwritesTarget()
    {
        var project = new ProjectDocument { Pieces = [new() { Name = "A\u0001B", Points = [new(0, 0), new(1, 1)] }] };
        var path = Target();
        Assert.Throws<InvalidDataException>(() => SvgFormat.Export(project, path));
        Assert.Equal("existing", File.ReadAllText(path));
    }
}
