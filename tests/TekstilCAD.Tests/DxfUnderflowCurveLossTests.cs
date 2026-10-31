using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfUnderflowCurveLossTests
{
    private static DxfImportResult Import(string geometry)
    {
        var dxf="0\nSECTION\n2\nENTITIES\n" + geometry +
            "0\nLINE\n8\nOTHER\n10\n0\n20\n2\n11\n10\n21\n2\n" +
            "0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(dxf));
        return DxfFormat.Import(stream,"underflow");
    }

    [Theory]
    [InlineData(42,"bulge")]
    [InlineData(40,"genişlik")]
    [InlineData(41,"genişlik")]
    [InlineData(43,"genişlik")]
    public void LightweightNonzeroUnderflowIsNotStraightened(int code,string expected)
    {
        var poly=$"0\nLWPOLYLINE\n90\n2\n10\n0\n20\n0\n{code}\n1e-400\n10\n10\n20\n0\n";
        var result=Import(poly);
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains(expected,StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(42,"bulge")]
    [InlineData(40,"genişlik")]
    [InlineData(41,"genişlik")]
    public void LegacyVertexNonzeroUnderflowIsNotStraightened(int code,string expected)
    {
        var poly=$"0\nPOLYLINE\n0\nVERTEX\n10\n0\n20\n0\n{code}\n-1e-400\n0\nVERTEX\n10\n10\n20\n0\n0\nSEQEND\n";
        var result=Import(poly);
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains(expected,StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.000e-400")]
    public void ExactZeroStillImports(string zero)
    {
        var result=Import($"0\nLWPOLYLINE\n90\n2\n10\n0\n20\n0\n42\n{zero}\n10\n10\n20\n0\n");
        Assert.Equal(2,result.Project.Pieces.Count);
    }
}
