using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfPrivateUnitHeaderTests
{
    private static DxfImportResult Read(string header)
    {
        var text="0\nSECTION\n2\nHEADER\n"+header+"0\nENDSEC\n"+
            "0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n10\n21\n0\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(text));
        return DxfFormat.Import(stream,"header");
    }

    [Fact]
    public void PrivateInsunitsIsNotTreatedAsDeclaredDrawingUnits()
    {
        var result=Read("102\n{APP\n9\n$INSUNITS\n70\n1\n102\n}\n");
        Assert.False(result.Units.IsReliable);
        Assert.Null(result.Units.Code);
        Assert.Contains(result.Warnings,w=>w.Contains("102 uygulama"));
    }

    [Fact]
    public void PrivateConflictingUnitsDoNotHideRealHeader()
    {
        var result=Read("9\n$INSUNITS\n70\n4\n102\n{APP\n9\n$INSUNITS\n70\n1\n102\n}\n");
        Assert.True(result.Units.IsReliable);
        Assert.Equal(4,result.Units.Code);
        Assert.Equal(1,result.Units.MillimetersPerUnit);
    }
}
