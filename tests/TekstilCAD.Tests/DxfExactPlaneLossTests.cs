using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfExactPlaneLossTests
{
    private static DxfImportResult Import(string entity)
    {
        var source="0\nSECTION\n2\nENTITIES\n"+entity+
            "0\nLINE\n8\nOTHER\n10\n0\n20\n2\n11\n10\n21\n2\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        return DxfFormat.Import(stream,"plane");
    }

    [Theory]
    [InlineData(30,"1e-400")]
    [InlineData(31,"-1e-400")]
    [InlineData(38,"1e-400")]
    [InlineData(39,"1e-400")]
    [InlineData(210,"1e-400")]
    [InlineData(220,"1e-400")]
    [InlineData(230,"1.00000000000000001")]
    public void RoundedPlaneValuesAreNotTreatedAsDefaults(int code,string value)
    {
        var result=Import($"0\nLINE\n10\n0\n20\n0\n11\n10\n21\n0\n{code}\n{value}\n");
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains("3D/elevation/normal"));
    }

    [Fact]
    public void UnderflowOnLegacyVertexSkipsWholePolyline()
    {
        var result=Import("0\nPOLYLINE\n0\nVERTEX\n10\n0\n20\n0\n30\n1e-400\n0\nVERTEX\n10\n10\n20\n0\n0\nSEQEND\n");
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains("3D VERTEX"));
    }

    [Fact]
    public void EquivalentDecimalDefaultsRemainSupported()
    {
        var result=Import("0\nLINE\n10\n0\n20\n0\n11\n10\n21\n0\n30\n-0.0e-400\n230\n100e-2\n");
        Assert.Equal(2,result.Project.Pieces.Count);
    }

    [Fact]
    public void UnsupportedPlaneDoesNotHideDuplicateLaterField()
    {
        Assert.Throws<InvalidDataException>(()=>Import("0\nLINE\n10\n0\n20\n0\n11\n10\n21\n0\n30\n1e-400\n230\n1\n230\n1\n"));
    }
}
