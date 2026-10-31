using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfExactDecimalCollisionTests
{
    private static DxfImportResult Import(string entities,double scale=1)
    {
        var data=Encoding.UTF8.GetBytes($"0\nSECTION\n2\nENTITIES\n{entities}0\nENDSEC\n0\nEOF\n");
        using var input=new MemoryStream(data,writable:false);
        var result=DxfFormat.Import(input,"sentetik",scale);
        Assert.Equal(data,input.ToArray());
        return result;
    }
    private static string Line(string start,string end,string layer="0") =>
        $"0\nLINE\n8\n{layer}\n6\nCONTINUOUS\n62\n1\n10\n{start}\n20\n0\n11\n{end}\n21\n0\n";
    private static string Lightweight(string a,string b) =>
        $"0\nLWPOLYLINE\n6\nCONTINUOUS\n90\n2\n10\n{a}\n20\n0\n10\n{b}\n20\n0\n";
    private static string Legacy(string a,string b) =>
        $"0\nPOLYLINE\n6\nCONTINUOUS\n70\n0\n0\nVERTEX\n10\n{a}\n20\n0\n0\nVERTEX\n10\n{b}\n20\n0\n0\nSEQEND\n";

    [Theory]
    [InlineData("1","1.0000000000000001")]
    [InlineData("1e-999","2e-999")]
    public void DifferentExactCoordinatesThatParseToSameDoubleCannotChain(string a,string b)
    {
        var ex=Assert.Throws<InvalidDataException>(()=>Import(Line("-1",a)+Line(b,"2")));
        Assert.Contains("hassasiyeti",ex.Message);
    }

    [Theory]
    [InlineData("1", "1.0")]
    [InlineData("10e-1", "+1E+000")]
    [InlineData(".5", "5e-1")]
    [InlineData("5.", "50e-1")]
    public void EquivalentDecimalSpellingsRemainConnected(string a,string b)
    {
        Assert.Single(Import(Line("-1",a)+Line(b,"10")).Project.Pieces);
    }

    [Fact]
    public void SignedZeroSpellingsAreTheSameSourcePoint()
    {
        var result=Import(Line("-1","-0")+Line("+0.0e10","1"));
        Assert.Single(result.Project.Pieces);
        var onlyZero=Assert.Throws<InvalidDataException>(()=>Import(Line("-0","+0.0")));
        Assert.Contains("sıfır uzunluklu LINE",onlyZero.Message);
    }

    [Theory]
    [InlineData("1","1.0000000000000001")]
    [InlineData("1e-999","2e-999")]
    public void BothPolylineFormsRejectCollapsedDifferentDecimalPoints(string a,string b)
    {
        Assert.Throws<InvalidDataException>(()=>Import(Lightweight(a,b)));
        Assert.Throws<InvalidDataException>(()=>Import(Legacy(a,b)));
    }

    [Fact]
    public void ExactRepeatedClosingVertexIsPreservedAcrossDifferentSpellings()
    {
        var lw="0\nLWPOLYLINE\n6\nCONTINUOUS\n70\n1\n90\n4\n10\n1\n20\n0\n10\n2\n20\n0\n10\n1\n20\n1\n10\n1.0\n20\n-0\n";
        var piece=Assert.Single(Import(lw).Project.Pieces);
        Assert.True(piece.Closed);Assert.Equal(piece.Points[0],piece.Points[^1]);
        var legacy="0\nPOLYLINE\n6\nCONTINUOUS\n70\n1\n"+
            "0\nVERTEX\n10\n1\n20\n0\n0\nVERTEX\n10\n2\n20\n0\n0\nVERTEX\n10\n1\n20\n1\n0\nVERTEX\n10\n1e0\n20\n-0.0\n0\nSEQEND\n";
        var legacyPiece=Assert.Single(Import(legacy).Project.Pieces);
        Assert.True(legacyPiece.Closed);Assert.Equal(legacyPiece.Points[0],legacyPiece.Points[^1]);
    }

    [Fact]
    public void DifferentLayersDoNotBecomeOneContour()
    {
        var result=Import(Line("0","1","A")+Line("1.0000000000000001","2","B"));
        Assert.Equal(2,result.Project.Pieces.Count);
    }

    [Theory]
    [InlineData("1e1000001")]
    [InlineData("1e-1000001")]
    public void ExcessiveExponentIsRejectedClearly(string number)
    {
        var ex=Assert.Throws<InvalidDataException>(()=>Import(Line("0",number)));
        Assert.Contains("üssü",ex.Message);
    }

    [Fact]
    public void ExcessiveCoordinateTokenLengthIsRejectedClearly()
    {
        var ex=Assert.Throws<InvalidDataException>(()=>Import(Line("0","1."+new string('0',256))));
        Assert.Contains("256 karakter",ex.Message);
    }
}
