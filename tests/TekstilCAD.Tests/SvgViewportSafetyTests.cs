using System.Globalization;
using System.Xml.Linq;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class SvgViewportSafetyTests
{
    private static string Target()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root not found.");
        var dir = Path.Combine(root.FullName, "work", "svg-viewport-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "drawing.svg");
    }

    [Fact]
    public void PhysicalViewportIncludesFullStrokeAndPreservesOneUnitPerMillimeter()
    {
        var path = Target();
        var project = new ProjectDocument { Pieces = [new PatternPiece { Closed = false,
            Points = [new(-20, -10), new(80, 40)] }] };
        SvgFormat.Export(project, path);
        var root = XDocument.Load(path).Root!;
        var box = root.Attribute("viewBox")!.Value.Split(' ').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        var width = double.Parse(root.Attribute("width")!.Value[..^2], CultureInfo.InvariantCulture);
        var height = double.Parse(root.Attribute("height")!.Value[..^2], CultureInfo.InvariantCulture);
        Assert.Equal(-20.125, box[0]); Assert.Equal(-10.125, box[1]);
        Assert.Equal(100.25, box[2]); Assert.Equal(50.25, box[3]);
        Assert.Equal(box[2], width); Assert.Equal(box[3], height);
        Assert.Equal(0.125, -20 - box[0]); Assert.Equal(0.125, box[0] + box[2] - 80);
        Assert.Equal(0.125, -10 - box[1]); Assert.Equal(0.125, box[1] + box[3] - 40);
    }

    [Fact]
    public void EmptyProjectDoesNotOverwriteExistingFile()
    {
        var path = Target(); File.WriteAllText(path, "existing");
        Assert.Throws<InvalidDataException>(() => SvgFormat.Export(new ProjectDocument(), path));
        Assert.Equal("existing", File.ReadAllText(path));
    }
}
