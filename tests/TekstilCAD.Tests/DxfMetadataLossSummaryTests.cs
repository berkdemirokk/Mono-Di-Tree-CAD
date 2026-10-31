using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfMetadataLossSummaryTests
{
    private static DxfImportResult Read(string fields)
    {
        var source="0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n10\n21\n0\n"+
            fields+"0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        return DxfFormat.Import(stream,"loss");
    }

    [Fact]
    public void SummaryReportsSourceGroupOccurrencesNotEntityCount()
    {
        var result=Read("370\n25\n440\n33554560\n1001\nAPP\n1000\nvalue\n102\n{APP\n102\n{NESTED\n102\n}\n102\n}\n");
        Assert.Equal(new DxfMetadataLossSummary(2,2,1,1),result.MetadataLoss);
        Assert.Single(result.Project.Pieces);
    }

    [Fact]
    public void CompletedImportDistinguishesZeroOccurrencesFromUninspectedResult()
    {
        Assert.Equal(new DxfMetadataLossSummary(0,0,0,0),Read("").MetadataLoss);
        Assert.Null(new DxfImportResult(new ProjectDocument(),[]).MetadataLoss);
    }

    [Fact]
    public void PrivateGroupCodesDoNotMasqueradeAsAppearanceOrExtendedData()
    {
        var result=Read("102\n{APP\n370\n25\n440\n123\n1001\nAPP\n1000\nprivate\n102\n}\n");
        Assert.Equal(new DxfMetadataLossSummary(0,1,0,0),result.MetadataLoss);
        Assert.Contains(result.Warnings,w=>w.Contains("102 uygulama veri grupları"));
        Assert.DoesNotContain(result.Warnings,w=>w.Contains("çizgi kalınlığı veya saydamlık"));
        Assert.DoesNotContain(result.Warnings,w=>w.Contains("genişletilmiş meta verileri"));
    }

    [Fact]
    public void MetadataOutsidePrivateGroupIsStillCounted()
    {
        var result=Read("102\n{APP\n370\n25\n440\n123\n1000\nprivate\n102\n}\n370\n50\n440\n33554560\n1001\nAPP\n1000\npublic\n");
        Assert.Equal(new DxfMetadataLossSummary(2,1,1,1),result.MetadataLoss);
    }
}
