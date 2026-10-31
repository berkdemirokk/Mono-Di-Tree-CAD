using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfNegativeEntityColorTests
{
    private static DxfImportResult Import(string entities,double factor=1)
    {
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes($"0\nSECTION\n2\nENTITIES\n{entities}0\nENDSEC\n0\nEOF\n"));
        return DxfFormat.Import(stream,"sentetik",factor);
    }

    private static string Line(string fields="",int y=0) =>
        $"0\nLINE\n6\nCONTINUOUS\n10\n0\n20\n{y}\n11\n10\n21\n{y}\n{fields}";

    private static string Polyline(string header="",string vertex="") =>
        $"0\nPOLYLINE\n6\nCONTINUOUS\n{header}0\nVERTEX\n10\n0\n20\n0\n{vertex}0\nVERTEX\n10\n10\n20\n0\n0\nSEQEND\n";

    [Theory]
    [InlineData("62\n-1\n")]
    [InlineData("62\n-255\n")]
    [InlineData("62\n-3\n420\n16711680\n")]
    public void NegativeEntityColorSkipsLineEvenWhenTrueColorIsPresent(string fields)
    {
        var result=Import(Line(fields)+Line(y:10));
        var survivor=Assert.Single(result.Project.Pieces);
        Assert.All(survivor.Points,p=>Assert.Equal(10,p.Y));
        Assert.Contains(result.Warnings,w=>w.Contains("negatif renk kodlu kapalı DXF nesnesi",StringComparison.Ordinal));
    }

    [Fact]
    public void HiddenPolylineConsumesVerticesAndKeepsOtherGeometry()
    {
        var result=Import(Polyline("62\n-1\n")+Line(y:10));
        var survivor=Assert.Single(result.Project.Pieces);
        Assert.All(survivor.Points,p=>Assert.Equal(10,p.Y));
        Assert.Contains(result.Warnings,w=>w.Contains("negatif renk kodlu",StringComparison.Ordinal));
        Assert.DoesNotContain(result.Warnings,w=>w.Contains("VERTEX",StringComparison.Ordinal));
    }

    [Fact]
    public void HiddenVertexSkipsWholePolylineWithoutDiscardingOtherLines()
    {
        var result=Import(Polyline(vertex:"62\n-1\n")+Line(y:10));
        var survivor=Assert.Single(result.Project.Pieces);
        Assert.All(survivor.Points,p=>Assert.Equal(10,p.Y));
        Assert.Contains(result.Warnings,w=>w.Contains("negatif renk kodlu",StringComparison.Ordinal));
    }

    [Fact]
    public void HiddenLightweightPolylineIsSkipped()
    {
        var poly="0\nLWPOLYLINE\n6\nCONTINUOUS\n62\n-1\n90\n2\n10\n0\n20\n0\n10\n10\n20\n0\n";
        var result=Import(poly+Line(y:10));
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains("negatif renk kodlu",StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyHiddenGeometryIsRejectedWithReason()
    {
        var error=Assert.Throws<InvalidDataException>(()=>Import(Line("62\n-1\n")));
        Assert.Contains("negatif renk kodlu",error.Message,StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("62\n-256\n")]
    [InlineData("62\n257\n")]
    [InlineData("62\n-1\n62\n1\n")]
    [InlineData("62\n-1.5\n")]
    public void InvalidOrRepeatedEntityColorIsRejected(string fields)
    {
        Assert.Throws<InvalidDataException>(()=>Import(Line(fields)+Line(y:10)));
    }

    [Fact]
    public void ScaledOverflowingLineIsRejectedInsteadOfMistakenForZero()
    {
        var overflowing="0\nLINE\n6\nCONTINUOUS\n10\n1e308\n20\n0\n11\n1e308\n21\n0\n";
        var error=Assert.Throws<InvalidDataException>(()=>Import(Line(y:10)+overflowing,2));
        Assert.Contains("koordinatları",error.Message,StringComparison.Ordinal);
    }

    [Fact]
    public void DistinctSourceEndpointsCollapsedByScaleAreRejected()
    {
        var collapsed="0\nLINE\n6\nCONTINUOUS\n10\n1e-300\n20\n0\n11\n2e-300\n21\n0\n";
        var error=Assert.Throws<InvalidDataException>(()=>Import(Line(y:10)+collapsed,1e-50));
        Assert.Contains("hassasiyeti",error.Message,StringComparison.Ordinal);
    }
}
