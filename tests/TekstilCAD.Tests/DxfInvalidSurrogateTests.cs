using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfInvalidSurrogateTests
{
    [Fact]
    public void InvalidSurrogatesAreDisclosedAndNormalizedBeforeCollisionChecks()
    {
        static PatternPiece Piece(string name) => new() { Name = name, Closed = false, Points = [new(0, 0), new(1, 1)] };
        var project = new ProjectDocument { Pieces = [Piece("x\uD800"), Piece("x\uDC00"), Piece("x_"), Piece("x😀")] };
        var plan = DxfFormat.PlanLayers(project);
        Assert.Equal("x\uD800", project.Pieces[0].Name);
        Assert.Equal("x\uDC00", project.Pieces[1].Name);
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Validate(project));
        Assert.Equal(new[] { "x_", "x__2", "x__3", "x😀" }, plan.Select(p => p.LayerName));
        Assert.All(plan.Take(3), mapping => Assert.True(mapping.Changed));
        Assert.False(plan[3].Changed);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var dir = Path.Combine(root!.FullName, "work", "dxf-surrogate-tests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".dxf");
        try
        {
            DxfFormat.Export(project, path);
            Assert.Equal(plan.Select(p => p.LayerName), DxfFormat.Import(path).Project.Pieces.Select(p => p.Name));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
