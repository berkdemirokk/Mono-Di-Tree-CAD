using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfMixedImportSafetyTests
{
    private static DxfImportResult Import(string entities)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"0\nSECTION\n2\nENTITIES\n{entities}0\nENDSEC\n0\nEOF\n"));
        return DxfFormat.Import(stream, "Karma");
    }

    private const string ValidLine = "0\nLINE\n8\nTemel\n10\n0\n20\n0\n11\n100\n21\n0\n";
    private const string BulgedPolyline = "0\nLWPOLYLINE\n8\nEgri\n90\n2\n10\n0\n20\n0\n42\n0.5\n10\n100\n20\n0\n";
    private const string CurveFittedPolyline = "0\nPOLYLINE\n70\n2\n0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n50\n20\n20\n0\nSEQEND\n";

    [Fact]
    public void SupportedLineIsPreservedAndEachUnsupportedKindIsReported()
    {
        var result = Import(ValidLine + BulgedPolyline + CurveFittedPolyline + "0\nCIRCLE\n10\n0\n20\n0\n40\n5\n");
        var line = Assert.Single(result.Project.Pieces);
        Assert.False(line.Closed);
        Assert.Equal(new[] { new Point2(0, 0), new Point2(100, 0) }, line.Points);
        var warnings = string.Join(" ", result.Warnings);
        Assert.Contains("bulge", warnings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CIRCLE", warnings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("POLYLINE", warnings, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("0\nLWPOLYLINE\n90\n3\n10\n0\n20\n0\n10\n10\n20\n0\n")]
    [InlineData("0\nPOLYLINE\n0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n")]
    [InlineData("0\nLINE\n10\n0\n10\n2\n20\n0\n11\n10\n21\n0\n")]
    public void MalformedSupportedEntityRejectsWholeMixedImport(string malformed)
    {
        Assert.Throws<InvalidDataException>(() => Import(ValidLine + malformed));
    }
}
