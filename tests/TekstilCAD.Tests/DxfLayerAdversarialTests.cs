using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfLayerAdversarialTests
{
    private static PatternPiece Piece(string name) => new() { Name = name, Closed = false, Points = [new(0, 0), new(10, 0)] };

    [Fact]
    public void LongCollisionsWhitespaceAndSurrogateBoundaryMatchWrittenLayers()
    {
        var prefix = new string('x', 254);
        var names = new[] { prefix + "😀", prefix + "😀", "  panel  ", "PANEL", " ", "0", "a/b", "a?b" };
        var document = new ProjectDocument { Pieces = names.Select(Piece).ToList() };
        var before = document.Pieces.Select(p => p.Name).ToArray();
        var plan = DxfFormat.PlanLayers(document);

        Assert.Equal(names.Length, plan.Count);
        Assert.Equal(plan.Count, plan.Select(p => p.LayerName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(plan, p => {
            Assert.InRange(p.LayerName.Length, 1, 255);
            Assert.False(char.IsHighSurrogate(p.LayerName[^1]));
            Assert.Equal(p.LayerName.Trim(), p.LayerName);
        });
        Assert.Equal(prefix, plan[0].LayerName);
        Assert.EndsWith("_2", plan[1].LayerName);
        Assert.Equal("panel", plan[2].LayerName);
        Assert.Equal("PANEL_2", plan[3].LayerName);
        Assert.Equal("0", plan[4].LayerName);
        Assert.Equal("0_2", plan[5].LayerName);

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var dir = Path.Combine(root!.FullName, "work", "dxf-layer-adversarial");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".dxf");
        try
        {
            DxfFormat.Export(document, path);
            var imported = DxfFormat.Import(path);
            Assert.Equal(plan.Select(p => p.LayerName), imported.Project.Pieces.Select(p => p.Name));
            Assert.Equal(before, document.Pieces.Select(p => p.Name));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
