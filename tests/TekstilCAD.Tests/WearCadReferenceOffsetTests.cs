using System.Security.Cryptography;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class WearCadReferenceOffsetTests
{
    [Fact]
    public void ImportedReferenceEdgesFormSquareAndOffsetByFiveMillimeters()
    {
        // Independent copy of the user's read-only 10x10 cm reference. The DXF contains
        // four LINE entities which the importer assembles into one closed contour.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var path = Path.Combine(root!.FullName, "tests", "TekstilCAD.Tests", "Fixtures", "wearcad-100mm-square.dxf");
        var bytes = File.ReadAllBytes(path);
        Assert.Equal("74B74B88B93ACB4A53BDD5225926F2A3F0DCCD2010E6295F3068B77AEDF84214", Convert.ToHexString(SHA256.HashData(bytes)));
        var imported = DxfFormat.Import(path, 1);
        var square = Assert.Single(imported.Project.Pieces);
        Assert.True(square.Closed);
        Assert.Equal(4, square.Points.Count);
        var originalBounds = Geometry.Bounds(square.Points);
        Assert.Equal(100, originalBounds.Width, 6);
        Assert.Equal(100, originalBounds.Height, 6);
        var source = square.Points.ToArray();
        var bounds = Geometry.Bounds(PatternTools.Offset(square, 5).Points);
        Assert.Equal(-5, bounds.MinX, 6); Assert.Equal(-5, bounds.MinY, 6);
        Assert.Equal(105, bounds.MaxX, 6); Assert.Equal(105, bounds.MaxY, 6);
        Assert.Equal(source, square.Points);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }
}
