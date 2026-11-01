using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfImportClosureNoticeTests
{
    [Fact]
    public void ExtraClosureAndTextAreReportedWithoutChangingSourceVertices()
    {
        const string dxf="0\nSECTION\n2\nENTITIES\n0\nPOLYLINE\n70\n1\n"+
            "0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n"+
            "0\nVERTEX\n10\n0\n20\n10\n0\nVERTEX\n10\n0\n20\n0\n"+
            "0\nVERTEX\n10\n0\n20\n0\n0\nSEQEND\n0\nTEXT\n1\nexample\n"+
            "0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(dxf));
        var result=DxfFormat.Import(stream,"fixture");
        var piece=Assert.Single(result.Project.Pieces);
        Assert.Equal(5,piece.Points.Count);
        Assert.Equal(piece.Points[0],piece.Points[^1]);
        Assert.Equal(piece.Points[0],piece.Points[^2]);
        Assert.Contains(result.Warnings,w=>w.Contains("1 kapalı konturda") && w.Contains("DXF dışa aktarmadan"));
        Assert.Contains(result.Warnings,w=>w.Contains("metin ve öznitelik") && w.Contains("dönüştürülmez"));
        Assert.Throws<InvalidDataException>(()=>DxfFormat.PlanLayers(result.Project));
        Assert.Equal(5,piece.Points.Count);
    }

    [Fact]
    public void OneExplicitClosureDoesNotProduceExtraClosureWarning()
    {
        const string dxf="0\nSECTION\n2\nENTITIES\n0\nPOLYLINE\n70\n1\n"+
            "0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n"+
            "0\nVERTEX\n10\n0\n20\n10\n0\nVERTEX\n10\n0\n20\n0\n"+
            "0\nSEQEND\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(dxf));
        var result=DxfFormat.Import(stream,"fixture");
        Assert.DoesNotContain(result.Warnings,w=>w.Contains("başlangıç noktası sonda birden fazla"));
        Assert.Single(DxfFormat.PlanLayers(result.Project));
    }
}
