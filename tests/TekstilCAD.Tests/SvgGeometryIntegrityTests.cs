using System.Xml.Linq;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class SvgGeometryIntegrityTests
{
    private static string NewTarget()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root not found.");
        var dir = Path.Combine(root.FullName, "work", "svg-geometry-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "pattern.svg");
    }

    [Fact]
    public void ExportProducesParseableMillimeterGeometryWithCorrectClosureAndUnicodeTitle()
    {
        var project = new ProjectDocument { Pieces = [
            new PatternPiece { Name = "Ön & <kol> 🧵", Closed = true, Points = [new(-10, -5), new(90, -5), new(90, 45), new(-10, 45)] },
            new PatternPiece { Name = "Açık çizgi", Closed = false, Points = [new(0, 0), new(12.5, 20)] }
        ] };
        var before = project.Clone();
        var path = NewTarget();
        SvgFormat.Export(project, path);
        var xml = XDocument.Load(path);
        XNamespace svg = "http://www.w3.org/2000/svg";
        var root = Assert.IsType<XElement>(xml.Root);
        Assert.Equal(svg + "svg", root.Name);
        Assert.Equal("100.25mm", (string?)root.Attribute("width"));
        Assert.Equal("50.25mm", (string?)root.Attribute("height"));
        Assert.Equal("-10.125 -5.125 100.25 50.25", (string?)root.Attribute("viewBox"));
        var paths = root.Elements(svg + "path").ToArray();
        Assert.Equal(2, paths.Length);
        Assert.Equal("M -10 -5 L 90 -5 L 90 45 L -10 45 Z", (string?)paths[0].Attribute("d"));
        Assert.Equal("M 0 0 L 12.5 20", (string?)paths[1].Attribute("d"));
        Assert.Equal("Ön & <kol> 🧵", paths[0].Element(svg + "title")?.Value);
        Assert.True(DocumentSession.ContentEquals(before, project));
    }

    [Theory]
    [InlineData("bad\u0001name")]
    [InlineData("bad\uFFFEname")]
    public void InvalidXmlNameLeavesExistingTargetUntouched(string name)
    {
        var path = NewTarget(); File.WriteAllText(path, "önceki veri");
        var before = File.ReadAllBytes(path);
        var project = new ProjectDocument { Pieces = [new PatternPiece { Name = name, Closed = false, Points = [new(0, 0), new(1, 1)] }] };
        Assert.Throws<InvalidDataException>(() => SvgFormat.Export(project, path));
        Assert.Equal(before, File.ReadAllBytes(path));
    }
}
