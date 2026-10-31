using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfRequiredCoordinateTests
{
    private static DxfImportResult Import(string entities)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes($"0\nSECTION\n2\nENTITIES\n{entities}0\nENDSEC\n0\nEOF\n"));
        return DxfFormat.Import(input, "örnek");
    }
    private static string Pair(int code) => $"{code}\n0\n";

    [Theory]
    [InlineData(10)] [InlineData(20)] [InlineData(11)] [InlineData(21)]
    public void LineRequiresEveryEndpointCoordinate(int missing)
    {
        var line = "0\nLINE\n" + string.Concat(new[] {10,20,11,21}.Where(c => c != missing).Select(Pair));
        var error = Assert.Throws<InvalidDataException>(() => Import(line));
        Assert.Contains(missing.ToString(), error.Message);
    }

    [Theory]
    [InlineData(10)] [InlineData(20)]
    public void PolylineVertexRequiresBothCoordinates(int missing)
    {
        var vertex = "0\nVERTEX\n" + string.Concat(new[] {10,20}.Where(c => c != missing).Select(Pair));
        var error = Assert.Throws<InvalidDataException>(() => Import("0\nPOLYLINE\n70\n0\n" + vertex + "0\nVERTEX\n10\n2\n20\n3\n0\nSEQEND\n"));
        Assert.Contains(missing.ToString(), error.Message);
    }

    [Fact]
    public void ExplicitZerosAreValidForLineAndPolyline()
    {
        var result = Import("0\nLINE\n10\n0\n20\n0\n11\n3\n21\n0\n0\nPOLYLINE\n70\n0\n0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n0\n20\n4\n0\nSEQEND\n");
        Assert.Equal(2, result.Project.Pieces.Count);
        Assert.All(result.Project.Pieces, p => Assert.Contains(new Point2(0, 0), p.Points));
    }
}
