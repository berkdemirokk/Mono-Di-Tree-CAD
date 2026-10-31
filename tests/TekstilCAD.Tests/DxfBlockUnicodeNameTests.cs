using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfBlockUnicodeNameTests
{
    private const string BlockLine="0\nLINE\n8\n0\n10\n0\n20\n0\n11\n10\n21\n0\n";
    private const string ModelLine="0\nLINE\n8\n0\n10\n20\n20\n0\n11\n30\n21\n0\n";
    private static string Drawing(string blockData)=>"0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n"+blockData+
        BlockLine+"0\nENDBLK\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n"+ModelLine+"0\nENDSEC\n0\nEOF\n";
    private static DxfImportResult Import(string text)
    {
        using var input=new MemoryStream(Encoding.UTF8.GetBytes(text));return DxfFormat.Import(input,"block");
    }

    [Theory]
    [InlineData("*Paper_Space0")]
    [InlineData(@"\U+002APaper_Space0")]
    [InlineData(@"*Paper\U+005FSpace0")]
    public void EscapedAndLiteralPaperBlockNeverBecomeProductionGeometry(string name)
    {
        var bytes=Encoding.UTF8.GetBytes(Drawing($"2\n{name}\n"));using var input=new MemoryStream(bytes);
        var result=DxfFormat.Import(input,"paper");
        var model=Assert.Single(result.Project.Pieces);
        Assert.Equal(new[]{new Point2(20,0),new Point2(30,0)},model.Points);
        Assert.Contains(result.Warnings,w=>w.Contains("kağıt alanı blok geometrisi"));
        Assert.Equal(bytes,input.ToArray());
    }

    [Theory]
    [InlineData(@"Par\U+00E7a","Parça")]
    [InlineData(@"A\U+D83D\U+DCA1","A💡")]
    [InlineData("A💡","A💡")]
    public void OrdinaryBlockNameIsDecodedWithoutChangingGeometry(string encoded,string expected)
    {
        var result=Import(Drawing($"2\n{encoded}\n"));
        Assert.Equal(2,result.Project.Pieces.Count);
        var piece=Assert.Single(result.Project.Pieces,p=>p.Name==expected+" / 0");
        Assert.Equal(new[]{new Point2(0,0),new Point2(10,0)},piece.Points);
        Assert.Contains(result.Warnings,w=>w.Contains("INSERT") && w.Contains("katman görünürlüğü mirası"));
    }

    [Theory]
    [InlineData("2\nA\n2\nB\n")]
    [InlineData("2\n A\n")]
    [InlineData("2\nA\\U+123\n")]
    [InlineData("2\nA\\U+D800\n")]
    [InlineData("2\nA\\U+000A\n")]
    [InlineData("2\nA\\folder\n")]
    [InlineData("")]
    public void MalformedOrAmbiguousBlockNameIsRejected(string fields)
        => Assert.Throws<InvalidDataException>(()=>Import(Drawing(fields)));

    [Fact]
    public void ApplicationGroupBlockNamesCannotOverrideActualName()
    {
        var result=Import(Drawing("102\n{APP\n2\n*Paper_Space0\n102\n}\n2\nActual\n"));
        Assert.Equal(2,result.Project.Pieces.Count);
        Assert.Contains(result.Project.Pieces,p=>p.Name=="Actual / 0");
        Assert.DoesNotContain(result.Warnings,w=>w.Contains("kağıt alanı blok geometrisi"));
    }
}
