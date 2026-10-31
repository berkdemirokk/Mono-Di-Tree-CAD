using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfLightweightBulgeValidationTests
{
    private static DxfImportResult Read(string vertices, bool approximate)
    {
        var dxf = "0\nSECTION\n2\nENTITIES\n0\nLWPOLYLINE\n90\n2\n" + vertices +
                  "0\nENDSEC\n0\nEOF\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(dxf));
        return approximate
            ? DxfFormat.ImportWithBulgeApproximation(stream, "test", 1, DxfTextEncoding.Utf8, 0.1)
            : DxfFormat.Import(stream, "test", 1);
    }

    [Theory]
    [InlineData("42\n0\n10\n0\n20\n0\n10\n10\n20\n0\n")]
    [InlineData("10\n0\n42\n0\n20\n0\n10\n10\n20\n0\n")]
    [InlineData("10\n0\n20\n0\n42\n0\n42\n0\n10\n10\n20\n0\n")]
    [InlineData("10\n0\n20\n0\n42\n0\n42\n1\n10\n10\n20\n0\n")]
    public void AmbiguousBulgeRecordsFailInBothModes(string vertices)
    {
        Assert.Throws<InvalidDataException>(() => Read(vertices, false));
        Assert.Throws<InvalidDataException>(() => Read(vertices, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneZeroBulgePerVertexRemainsAValidStraightPolyline(bool approximate)
    {
        var result = Read("10\n0\n20\n0\n42\n0\n10\n10\n20\n0\n42\n0\n", approximate);
        Assert.Equal(2, Assert.Single(result.Project.Pieces).Points.Count);
    }
}
