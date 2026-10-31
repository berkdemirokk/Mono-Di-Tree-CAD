using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfCoordinateUnderflowTests
{
    private static void Import(string entity, double scale = 1)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"0\nSECTION\n2\nENTITIES\n{entity}0\nENDSEC\n0\nEOF\n"));
        DxfFormat.Import(stream, "test", scale);
    }

    [Theory]
    [InlineData("1e-400", 1)]
    [InlineData("1e-322", 0.001)]
    public void RejectsCoordinateThatUnderflowsEvenWhenEndpointsRemainDistinct(string coordinate, double scale)
    {
        var line = $"0\nLINE\n8\n0\n6\nCONTINUOUS\n10\n{coordinate}\n20\n1\n11\n2\n21\n1\n";
        var lightweight = $"0\nLWPOLYLINE\n8\n0\n6\nCONTINUOUS\n90\n2\n10\n{coordinate}\n20\n1\n10\n2\n20\n1\n";
        var classic = $"0\nPOLYLINE\n8\n0\n6\nCONTINUOUS\n0\nVERTEX\n10\n{coordinate}\n20\n1\n0\nVERTEX\n10\n2\n20\n1\n0\nSEQEND\n";
        foreach (var entity in new[] { line, lightweight, classic })
            Assert.Contains("sıfıra düştü", Assert.Throws<InvalidDataException>(() => Import(entity, scale)).Message);
    }

    [Fact]
    public void ExactZeroAndSmallRepresentableCoordinatesRemainValid()
    {
        Import("0\nLINE\n8\n0\n6\nCONTINUOUS\n10\n-0\n20\n1\n11\n1e-9\n21\n1\n");
    }
}
