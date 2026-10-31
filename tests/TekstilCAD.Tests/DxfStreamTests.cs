using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfStreamTests
{
    [Fact]
    public void StreamImportUsesProvidedSnapshotAndLeavesStreamOpen()
    {
        const string dxf = "0\nSECTION\n2\nENTITIES\n0\nLWPOLYLINE\n90\n2\n70\n0\n10\n1\n20\n2\n10\n11\n20\n12\n0\nENDSEC\n0\nEOF\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(dxf));
        var result = DxfFormat.Import(stream, "snapshot", 1);
        Assert.True(stream.CanRead);
        Assert.Equal("snapshot", result.Project.Name);
        var piece = Assert.Single(result.Project.Pieces);
        Assert.Equal(2, piece.Points.Count);
        Assert.Equal(11, piece.Points[1].X);
    }
}
