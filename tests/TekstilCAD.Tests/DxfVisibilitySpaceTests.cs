using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfVisibilitySpaceTests
{
    private static string Line(string groups,int start,int end) => $"0\nLINE\n8\nKALIP\n{groups}10\n{start}\n20\n0\n11\n{end}\n21\n0\n";
    private static string Polyline(string header,string vertex,string tail="0\nSEQEND\n") =>
        $"0\nPOLYLINE\n8\nKALIP\n70\n0\n{header}0\nVERTEX\n10\n0\n20\n5\n{vertex}0\nVERTEX\n10\n10\n20\n5\n{tail}";
    private static DxfImportResult Import(string body)
    {
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(body));
        return DxfFormat.Import(stream,"space");
    }
    private static string Entities(string body)=>$"0\nSECTION\n2\nENTITIES\n{body}0\nENDSEC\n0\nEOF\n";

    [Fact]
    public void InvisibleAndPaperLinesNeverJoinVisibleModelLines()
    {
        var data=Line("67\n0\n410\nModel\n60\n0\n",0,10)+Line("67\n1\n",10,20)+Line("60\n1\n",20,30)+Line("",30,40);
        var result=Import(Entities(data));
        Assert.Equal(2,result.Project.Pieces.Count);
        Assert.Contains(result.Warnings,w=>w.Contains("kağıt alanı DXF nesnesi"));
        Assert.Contains(result.Warnings,w=>w.Contains("görünmez DXF nesnesi"));
        Assert.Contains(result.Project.Pieces,p=>p.Points.SequenceEqual(new[]{new Point2(0,0),new Point2(10,0)}));
        Assert.Contains(result.Project.Pieces,p=>p.Points.SequenceEqual(new[]{new Point2(30,0),new Point2(40,0)}));
    }

    [Theory]
    [InlineData("410\nLayout1\n")]
    [InlineData("67\n0\n410\nLayout1\n")]
    public void NonModelOrConflictingLayoutIsSkippedWithExplicitUncertainty(string group)
    {
        var result=Import(Entities(Line(group,0,10)+Line("",20,30)));
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains("model dışı veya belirsiz yerleşim"));
    }

    [Theory]
    [InlineData("60\n1\n","")]
    [InlineData("67\n1\n","")]
    [InlineData("","60\n1\n")]
    [InlineData("","67\n1\n")]
    public void ExcludedLegacyPolylineNeverLeaksVerticesOrConsumesFollowingLine(string header,string vertex)
    {
        var result=Import(Entities(Polyline(header,vertex)+Line("",20,30)));
        var only=Assert.Single(result.Project.Pieces);
        Assert.Equal(new[]{new Point2(20,0),new Point2(30,0)},only.Points);
        Assert.Contains(result.Warnings,w=>w.Contains("kontur atlandı"));
    }

    [Fact]
    public void ExcludedPolylineStillRequiresSequenceEnd()
    {
        var malformed=Polyline("60\n1\n","",tail:"")+Line("",20,30);
        Assert.Contains("SEQEND",Assert.Throws<InvalidDataException>(()=>Import(Entities(malformed))).Message);
    }

    [Fact]
    public void PaperBlockIsSkippedButOrdinaryBlockAndModelLineArePreserved()
    {
        var blocks="0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\n*Paper_Space0\n"+Line("",0,10)+"0\nENDBLK\n"+
            "0\nBLOCK\n2\nParça\n"+Line("",20,30)+"0\nENDBLK\n0\nENDSEC\n";
        var result=Import(blocks+Entities(Line("",40,50)));
        Assert.Equal(2,result.Project.Pieces.Count);
        Assert.Contains(result.Warnings,w=>w.Contains("kağıt alanı blok geometrisi"));
        Assert.Contains(result.Warnings,w=>w.Contains("BLOCK tanım geometrisi"));
    }

    [Theory]
    [InlineData("60\n2\n")]
    [InlineData("67\nnot-integer\n")]
    [InlineData("67\n0\n67\n1\n")]
    [InlineData("410\n \n")]
    [InlineData("410\nModel\n410\nLayout1\n")]
    public void InvalidOrDuplicateFlagsAreRejected(string groups)
        => Assert.Throws<InvalidDataException>(()=>Import(Entities(Line(groups,0,10)+Line("",20,30))));

    [Fact]
    public void ApplicationGroupContentsAreNotTreatedAsEntityVisibility()
    {
        var model=Line("102\n{APP\n60\n1\n67\n1\n410\nLayout1\n102\n}\n",0,10);
        var result=Import(Entities(model));
        Assert.Single(result.Project.Pieces);
        Assert.DoesNotContain(result.Warnings,w=>w.Contains("kontur atlandı"));
        Assert.Throws<InvalidDataException>(()=>Import(Entities(Line("102\n{APP\n60\n1\n",0,10))));
    }

    [Fact]
    public void ApplicationDataCannotMaskActualEntityNormal()
    {
        var unsupportedNormal=Line("102\n{APP\n230\n1\n102\n}\n230\n-1\n",0,10);
        var result=Import(Entities(unsupportedNormal+Line("",20,30)));
        var only=Assert.Single(result.Project.Pieces);
        Assert.Equal(new[]{new Point2(20,0),new Point2(30,0)},only.Points);
        Assert.Contains(result.Warnings,w=>w.Contains("3D/elevation/normal"));
    }

    [Fact]
    public void ApplicationCoordinatesWidthsAndFlagsCannotChangeEntityGeometry()
    {
        var data="0\nLWPOLYLINE\n8\nMODEL\n102\n{APP\n90\n99\n10\n1000\n20\n1000\n43\n12\n42\n1\n70\n1\n102\n}\n"+
            "90\n2\n70\n0\n10\n0\n20\n0\n10\n10\n20\n0\n";
        var result=Import(Entities(data));var piece=Assert.Single(result.Project.Pieces);
        Assert.False(piece.Closed);Assert.Equal(new[]{new Point2(0,0),new Point2(10,0)},piece.Points);
        Assert.DoesNotContain(result.Warnings,w=>w.Contains("kontur atlandı"));
    }

    [Fact]
    public void VertexApplicationDataCannotMaskActualVertexPlane()
    {
        var poly=Polyline("","102\n{APP\n230\n1\n10\n999\n102\n}\n230\n-1\n");
        var result=Import(Entities(poly+Line("",20,30)));
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains("3D VERTEX"));
    }

    [Fact]
    public void AllExcludedEntitiesExplainFailureAndCancellationIsHonored()
    {
        var error=Assert.Throws<InvalidDataException>(()=>Import(Entities(Line("60\n1\n",0,10))));
        Assert.Contains("görünmez DXF nesnesi",error.Message);
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(Entities(Line("",0,10))));
        Assert.Throws<OperationCanceledException>(()=>DxfFormat.Import(stream,"cancel",1,cancellation.Token));
    }
}
