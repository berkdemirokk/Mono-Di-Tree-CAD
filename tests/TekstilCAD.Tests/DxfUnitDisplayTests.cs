using System.Globalization;
using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfUnitDisplayTests
{
    [Fact]
    public void InchFactorIsReadableAndUnusualSelectedFactorRetainsRoundTripPrecision()
    {
        const double factor = 1.0000000000000002;
        const string input = "0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n10\n21\n0\n0\nENDSEC\n0\nEOF\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        var result = DxfFormat.Import(stream, "inch", factor);
        var warning = Assert.Single(result.Warnings, w => w.Contains("uyuşmazlığı"));
        Assert.Contains("1 birim = 25.4 mm", warning);
        Assert.DoesNotContain("25.399999999999999", warning);
        Assert.Contains(factor.ToString("R", CultureInfo.InvariantCulture), warning);
        Assert.Equal(10 * factor, Math.Abs(result.Project.Pieces[0].Points[1].X - result.Project.Pieces[0].Points[0].X));
    }
}
