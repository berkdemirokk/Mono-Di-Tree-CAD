using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfLineTypeSafetyTests
{
    private static DxfImportResult Import(string entities,string tables="")
    {
        var dxf=$"0\nSECTION\n2\nTABLES\n{tables}0\nENDSEC\n0\nSECTION\n2\nENTITIES\n{entities}0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(dxf));
        return DxfFormat.Import(stream,"sentetik");
    }
    private static string Line(int a,int b,string type="",string layer="0") =>
        $"0\nLINE\n8\n{layer}\n10\n{a}\n20\n0\n11\n{b}\n21\n0\n{type}";
    private static string Layer(string name,string type) =>
        $"0\nTABLE\n2\nLAYER\n0\nLAYER\n2\n{name}\n70\n0\n62\n7\n6\n{type}\n0\nENDTAB\n";

    [Fact]
    public void ExplicitDashedLineIsSkippedAndReported()
    {
        var result=Import(Line(0,10,"6\nCONTINUOUS\n")+Line(10,20,"6\nDASHED\n"));
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains("çizgi tipi") && w.Contains("atlandı"));
        Assert.Equal(10,result.Project.Pieces[0].Points.Max(p=>p.X));
    }
    [Fact]
    public void ByLayerDashedIsSkippedAndReported()
    {
        var result=Import(Line(0,10,layer:"kesim")+Line(10,20,layer:"kat"),LayerTable());
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains("çizgi tipi") && w.Contains("atlandı"));
        Assert.Equal(10,result.Project.Pieces[0].Points.Max(p=>p.X));
        static string LayerTable()=>"0\nTABLE\n2\nLAYER\n"+
            "0\nLAYER\n2\nkesim\n70\n0\n62\n7\n6\nCONTINUOUS\n"+
            "0\nLAYER\n2\nkat\n70\n0\n62\n7\n6\nDASHED\n0\nENDTAB\n";
    }
    [Fact]
    public void MissingLayerTypeDoesNotJoinExplicitSolidToUnknownInheritance()
    {
        var result=Import(Line(0,10,"6\nCONTINUOUS\n")+Line(10,20));
        Assert.Equal(2,result.Project.Pieces.Count);
        Assert.Contains(result.Warnings,w=>w.Contains("çizgi tipi mirası doğrulanamadı"));
    }
    [Fact]
    public void ByBlockDoesNotJoinExplicitSolidAndIsDisclosed()
    {
        var result=Import(Line(0,10,"6\nCONTINUOUS\n")+Line(10,20,"6\nBYBLOCK\n"),Layer("0","CONTINUOUS"));
        Assert.Equal(2,result.Project.Pieces.Count);
        Assert.Contains(result.Warnings,w=>w.Contains("BYBLOCK"));
    }
    [Fact]
    public void KnownContinuousByLayerStillJoinsExplicitContinuous()
    {
        var result=Import(Line(0,10)+Line(10,20,"6\nCONTINUOUS\n"),Layer("0","CONTINUOUS"));
        Assert.Single(result.Project.Pieces);
        Assert.DoesNotContain(result.Warnings,w=>w.Contains("çizgi tipi"));
    }
    [Fact]
    public void DashedLegacyPolylineIsConsumedAndReported()
    {
        var poly="0\nPOLYLINE\n6\nDASHED\n70\n0\n0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n0\nSEQEND\n";
        var result=Import(Line(0,10,"6\nCONTINUOUS\n")+poly);
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains("çizgi tipi") && w.Contains("atlandı"));
        Assert.DoesNotContain(result.Warnings,w=>w.Contains("VERTEX"));
    }
    [Fact]
    public void DashedLightweightPolylineIsSkipped()
    {
        var poly="0\nLWPOLYLINE\n6\nDASHED\n90\n2\n10\n10\n20\n0\n10\n20\n20\n0\n";
        var result=Import(Line(0,10,"6\nCONTINUOUS\n")+poly);
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains("çizgi tipi") && w.Contains("atlandı"));
    }
    [Fact]
    public void OnlyDashedGeometryFailsWithAnExplicitSkipReason()
    {
        var ex=Assert.Throws<InvalidDataException>(()=>Import(Line(0,10,"6\nDASHED\n")));
        Assert.Contains("çizgi tipi",ex.Message);
        Assert.Contains("atlandı",ex.Message);
    }
    [Fact]
    public void DuplicateEntityOrLayerTypeIsRejected()
    {
        Assert.Throws<InvalidDataException>(()=>Import(Line(0,10,"6\nCONTINUOUS\n6\nDASHED\n")));
        var duplicate="0\nTABLE\n2\nLAYER\n0\nLAYER\n2\n0\n70\n0\n62\n7\n6\nCONTINUOUS\n6\nDASHED\n0\nENDTAB\n";
        Assert.Throws<InvalidDataException>(()=>Import(Line(0,10),duplicate));
    }
}
