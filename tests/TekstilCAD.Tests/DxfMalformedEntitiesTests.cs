using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfMalformedEntitiesTests
{
    private static DxfImportResult Import(string entities)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"0\nSECTION\n2\nENTITIES\n{entities}0\nENDSEC\n0\nEOF\n"));
        return DxfFormat.Import(stream, "kontrol");
    }

    [Fact]
    public void LightweightPolylineRequiresYForEveryX()
    {
        Assert.Throws<InvalidDataException>(() => Import("0\nLWPOLYLINE\n90\n2\n10\n0\n20\n0\n10\n3\n"));
    }

    [Fact]
    public void LightweightPolylineRejectsOrphanYRatherThanDiscardingIt()
    {
        Assert.Throws<InvalidDataException>(() => Import("0\nLWPOLYLINE\n90\n2\n20\n9\n10\n0\n20\n0\n10\n3\n20\n0\n"));
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void LightweightPolylineRejectsMalformedCoordinates(string value)
    {
        Assert.Throws<InvalidDataException>(() => Import($"0\nLWPOLYLINE\n90\n2\n10\n0\n20\n0\n10\n3\n20\n{value}\n"));
    }

    [Fact]
    public void ExplicitZeroCoordinatesRemainValid()
    {
        var result = Import("0\nLWPOLYLINE\n90\n2\n10\n0\n20\n0\n10\n3\n20\n0\n");
        Assert.Contains(new Point2(0, 0), Assert.Single(result.Project.Pieces).Points);
    }

    [Theory]
    [InlineData("ARC")]
    [InlineData("CIRCLE")]
    public void UnsupportedCurvesAreExplicitlyReportedRatherThanInvented(string type)
    {
        var result = Import($"0\n{type}\n10\n0\n20\n0\n0\nLINE\n10\n0\n20\n0\n11\n3\n21\n0\n");
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings, warning => warning.Contains(type, StringComparison.Ordinal));
    }
}
