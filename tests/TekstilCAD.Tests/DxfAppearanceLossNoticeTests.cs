using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfAppearanceLossNoticeTests
{
    private static DxfImportResult Read(string tableFields, string entityFields)
    {
        var dxf = "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n" +
            "0\nLAYER\n2\nCUT\n70\n0\n62\n7\n6\nCONTINUOUS\n" + tableFields +
            "0\nENDTAB\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n" +
            "0\nLINE\n8\nCUT\n10\n0\n20\n0\n11\n10\n21\n0\n" + entityFields +
            "0\nENDSEC\n0\nEOF\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(dxf));
        return DxfFormat.Import(stream, "appearance");
    }

    [Theory]
    [InlineData("370\n25\n", "")]
    [InlineData("440\n33554560\n", "")]
    [InlineData("", "370\n25\n")]
    [InlineData("", "440\n33554560\n")]
    public void AppearanceMetadataLossIsReported(string tableFields, string entityFields)
    {
        var result = Read(tableFields, entityFields);
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings, warning => warning.Contains("çizgi kalınlığı veya saydamlık meta verileri aktarılmaz"));
    }

    [Fact]
    public void NoAppearanceFieldsDoesNotReportAppearanceLoss()
    {
        var result = Read("", "");
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("çizgi kalınlığı veya saydamlık"));
    }
}
