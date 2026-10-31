using System.Globalization;
using System.Xml.Linq;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class SvgSharpJoinTests
{
    [Fact]
    public void SharpCornerUsesRoundJoinInsideStrokeMarginAtOneMillimeterPerUnit()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root not found.");
        var dir = Path.Combine(root.FullName, "work", "svg-sharp-join", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "sharp.svg");
        var project = new ProjectDocument { Pieces = [new PatternPiece { Points = [new(0, 0), new(100, 0), new(1, 1)] }] };
        SvgFormat.Export(project, path);
        var svg = XDocument.Load(path).Root!;
        var shape = svg.Elements().Single();
        Assert.Equal("round", (string?)shape.Attribute("stroke-linejoin"));
        Assert.Equal("round", (string?)shape.Attribute("stroke-linecap"));
        Assert.Equal("0.25", (string?)shape.Attribute("stroke-width"));
        var box = svg.Attribute("viewBox")!.Value.Split(' ').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(-0.125, box[0]); Assert.Equal(-0.125, box[1]);
        Assert.Equal(100.25, box[2]); Assert.Equal(1.25, box[3]);
        Assert.Equal(box[2], double.Parse(svg.Attribute("width")!.Value[..^2], CultureInfo.InvariantCulture));
        Assert.Equal(box[3], double.Parse(svg.Attribute("height")!.Value[..^2], CultureInfo.InvariantCulture));
    }
}
