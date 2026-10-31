using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfUnitPrecisionTests
{
    private static DxfImportResult Read(string header, double factor = 1)
    {
        var text = "0\nSECTION\n2\nHEADER\n" + header + "0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n1\n21\n0\n0\nENDSEC\n0\nEOF\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return DxfFormat.Import(stream, "units", factor);
    }

    [Theory]
    [InlineData(3, 1609344)]
    [InlineData(7, 1000000)]
    [InlineData(8, 0.0000254)]
    [InlineData(9, 0.0254)]
    [InlineData(11, 0.0000001)]
    [InlineData(12, 0.000001)]
    [InlineData(15, 10000)]
    [InlineData(16, 100000)]
    [InlineData(17, 1000000000000)]
    public void LessCommonKnownCodesAreReportedWithoutChangingSelectedScale(int code, double expected)
    {
        var result = Read($"9\n$INSUNITS\n70\n{code}\n");
        Assert.True(result.Units.IsReliable);
        Assert.Equal(expected, result.Units.MillimetersPerUnit);
        Assert.Equal(1, Math.Abs(result.Project.Pieces[0].Points[1].X - result.Project.Pieces[0].Points[0].X));
        Assert.Contains(result.Warnings, warning => warning.Contains("uyuşmazlığı"));
    }

    [Theory]
    [InlineData(1.0000000000005, false)]
    [InlineData(1.000000000002, true)]
    public void RelativeToleranceChangesWarningNotTheChosenConversion(double factor, bool mismatch)
    {
        var result = Read("9\n$INSUNITS\n70\n4\n", factor);
        Assert.Equal(mismatch, result.Warnings.Any(w => w.Contains("uyuşmazlığı")));
        Assert.Equal(factor, Math.Abs(result.Project.Pieces[0].Points[1].X - result.Project.Pieces[0].Points[0].X), 14);
    }

    [Theory]
    [InlineData("9\n$INSUNITS\n40\n4\n")]
    [InlineData("9\n$INSUNITS\n9\n$OTHER\n70\n4\n")]
    [InlineData("9\n$INSUNITS\n70\n18\n")]
    public void AmbiguousOrUnsupportedUnitsAreNeverTrusted(string header)
    {
        var result = Read(header);
        Assert.False(result.Units.IsReliable);
        Assert.Null(result.Units.MillimetersPerUnit);
        Assert.Contains(result.Warnings, w => w.Contains("güvenilir biçimde belirlenemedi"));
    }
}
