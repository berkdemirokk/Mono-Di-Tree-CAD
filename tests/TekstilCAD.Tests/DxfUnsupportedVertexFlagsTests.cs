using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfUnsupportedVertexFlagsTests
{
    private static DxfImportResult Import(string vertexFlag)
    {
        var text="0\nSECTION\n2\nENTITIES\n"+
            "0\nLINE\n10\n20\n20\n0\n11\n30\n21\n0\n"+
            "0\nPOLYLINE\n70\n1\n"+
            $"0\nVERTEX\n10\n0\n20\n0\n70\n{vertexFlag}\n"+
            "0\nVERTEX\n10\n10\n20\n0\n"+
            "0\nVERTEX\n10\n0\n20\n10\n"+
            "0\nSEQEND\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(text));
        return DxfFormat.Import(stream,"sentetik");
    }

    [Theory]
    [InlineData("4")]
    [InlineData("256")]
    [InlineData("260")]
    [InlineData("32767")]
    public void UnknownVertexFlagsRejectEntireImportRatherThanFlattening(string flags)
    {
        Assert.Throws<InvalidDataException>(()=>Import(flags));
    }

    [Fact]
    public void PlainVertexStillImportsAlongsideIndependentLine()
    {
        var result=Import("0");
        Assert.Equal(2,result.Project.Pieces.Count);
        Assert.Single(result.Project.Pieces,piece=>piece.Closed);
        Assert.Single(result.Project.Pieces,piece=>!piece.Closed);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("8")]
    [InlineData("16")]
    [InlineData("32")]
    [InlineData("64")]
    [InlineData("128")]
    public void SupportedButNonPlanarOrFittedVertexIsSkippedWithWarning(string flags)
    {
        var result=Import(flags);
        Assert.Single(result.Project.Pieces);
        Assert.False(result.Project.Pieces[0].Closed);
        Assert.Contains(result.Warnings,w=>w.StartsWith("Atlanan çoklu çizgi: 1.") && w.Contains("üretimde kullanmayın"));
    }
}
