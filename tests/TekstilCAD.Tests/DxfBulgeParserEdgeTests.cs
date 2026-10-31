using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfBulgeParserEdgeTests
{
    private static string Light(bool closed,string first,string second,string third) =>
        "0\nLWPOLYLINE\n8\nCUT\n90\n3\n70\n"+(closed?"1":"0")+"\n"+first+second+third;
    private static string Classic(bool closed,string first,string second,string third) =>
        "0\nPOLYLINE\n8\nCUT\n70\n"+(closed?"1":"0")+"\n0\nVERTEX\n"+first+"0\nVERTEX\n"+second+"0\nVERTEX\n"+third+"0\nSEQEND\n";
    private static string Vertex(string x,string y,string? bulge=null) => "10\n"+x+"\n20\n"+y+"\n"+(bulge is null ? "" : "42\n"+bulge+"\n");
    private static DxfImportResult Read(string body,double factor=1,double tolerance=0.1)
    {
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes("0\nSECTION\n2\nENTITIES\n"+body+"0\nENDSEC\n0\nEOF\n"));
        return DxfFormat.ImportWithBulgeApproximation(stream,"edge",factor,DxfTextEncoding.Utf8,tolerance);
    }
    private static string Poly(bool classic,bool closed,string a,string b,string c) => classic ? Classic(closed,a,b,c) : Light(closed,a,b,c);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BulgeOnSecondVertexBendsOnlyFollowingSegment(bool classic)
    {
        var source=Poly(classic,false,Vertex("0","0"),Vertex("10","0","1"),Vertex("20","0"));
        var points=Assert.Single(Read(source).Project.Pieces).Points;
        Assert.Equal(new Point2(0,0),points[0]);Assert.Equal(new Point2(10,0),points[1]);Assert.Equal(new Point2(20,0),points[^1]);
        Assert.True(points.Count>3);Assert.True(points.Skip(2).SkipLast(1).Any(p=>p.Y<0));
        Assert.All(points.Skip(1),p=>Assert.InRange(p.X,10-1e-9,20+1e-9));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LastVertexOfClosedPolylineControlsClosingArc(bool classic)
    {
        var source=Poly(classic,true,Vertex("0","0"),Vertex("10","0"),Vertex("10","10","1"));
        var piece=Assert.Single(Read(source).Project.Pieces);
        Assert.True(piece.Closed);Assert.Equal(new Point2(0,0),piece.Points[0]);
        Assert.Equal(new Point2(10,0),piece.Points[1]);Assert.Equal(new Point2(10,10),piece.Points[2]);
        Assert.True(piece.Points.Skip(3).Any(p=>p.X<5 && p.Y>5));
        Assert.NotEqual(piece.Points[0],piece.Points[^1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenLastNonzeroBulgeIsRejected(bool classic)
    {
        var source=Poly(classic,false,Vertex("0","0"),Vertex("10","0"),Vertex("20","0","1"));
        Assert.Throws<InvalidDataException>(()=>Read(source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scale254UsesMillimetresForChordError(bool classic)
    {
        var source=Poly(classic,false,Vertex("0","0","1"),Vertex("10","0"),Vertex("20","0"));
        var small=Read(source,1,0.1);var scaled=Read(source,25.4,0.1);
        var a=Assert.Single(small.Project.Pieces).Points;var b=Assert.Single(scaled.Project.Pieces).Points;
        Assert.True(b.Count>a.Count);Assert.Equal(new Point2(254,0),b.First(p=>p.X==254 && p.Y==0));
        Assert.Equal(new Point2(508,0),b[^1]);Assert.Equal(25.4,scaled.ImportParameters!.MillimetersPerUnit);
        var arcEnd=b.FindIndex(p=>p.X==254 && p.Y==0);Assert.True(arcEnd>1);
        for(int i=1;i<=arcEnd;i++)
        {
            var x=(b[i-1].X+b[i].X)/2-127;var y=(b[i-1].Y+b[i].Y)/2;
            var sagitta=127-Math.Sqrt(x*x+y*y);
            Assert.InRange(sagitta,-1e-9,0.1000001);
        }
    }

    [Theory]
    [InlineData("0\nINSERT\n2\nA\n60\n1\n")]
    [InlineData("0\nINSERT\n2\nA\n67\n1\n")]
    public void HiddenOrPaperInsertFailsClosed(string hiddenInsert)
    {
        Assert.Throws<InvalidDataException>(()=>Read("0\nLINE\n10\n0\n20\n0\n11\n1\n21\n0\n"+hiddenInsert));
    }

    [Fact]
    public void UnsupportedBulgedBlockFailsClosedEvenWhenUnused()
    {
        var source="0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n10\n0\n20\n0\n"+
            Light(false,Vertex("0","0","1"),Vertex("1","0"),Vertex("2","0"))+"0\nENDBLK\n0\nENDSEC\n"+
            "0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n1\n21\n0\n0\nENDSEC\n0\nEOF\n";
        using var input=new MemoryStream(Encoding.UTF8.GetBytes(source));
        Assert.Throws<InvalidDataException>(()=>DxfFormat.ImportWithBulgeApproximation(input,"block",1,DxfTextEncoding.Utf8,0.1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DecimalUnderflowAndOversizedApproximationFailClosed(bool classic)
    {
        var underflow=Poly(classic,false,Vertex("1e-999","0"),Vertex("10","0","1"),Vertex("20","0"));
        Assert.Throws<InvalidDataException>(()=>Read(underflow));
        var tooFine=Poly(classic,false,Vertex("10000","0","1"),Vertex("-10000","0"),Vertex("-9999","0"));
        Assert.Throws<InvalidDataException>(()=>Read(tooFine,1,0.00001));
        var overflow=Poly(classic,false,Vertex("1e309","0"),Vertex("10","0"),Vertex("20","0"));
        Assert.Throws<InvalidDataException>(()=>Read(overflow));
    }
}
