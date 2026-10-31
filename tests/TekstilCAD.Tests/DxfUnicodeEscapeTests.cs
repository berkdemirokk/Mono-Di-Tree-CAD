using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfUnicodeEscapeTests
{
    private static string Layer(string name)=>$"0\nLAYER\n2\n{name}\n70\n0\n62\n7\n";
    private static string Line(string layer,int x1,int x2)=>$"0\nLINE\n8\n{layer}\n10\n{x1}\n20\n0\n11\n{x2}\n21\n0\n";
    private static DxfImportResult Import(string layers,string geometry)
    {
        var text=$"0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n{layers}0\nENDTAB\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n{geometry}0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(text));
        return DxfFormat.Import(stream,"unicode");
    }

    [Fact]
    public void EscapedAndLiteralTurkishNamesResolveSameLayerBeforeJoining()
    {
        var result=Import(Layer(@"A\U+015F"),Line("Aş",0,10)+Line(@"a\U+015F",10,20));
        var piece=Assert.Single(result.Project.Pieces);
        Assert.Equal(new[]{new Point2(0,0),new Point2(10,0),new Point2(20,0)},piece.Points);
        Assert.Equal("Aş",piece.Name);
        Assert.DoesNotContain(result.Warnings,w=>w.Contains("katman görünürlüğü bilinmiyor"));
    }

    [Fact]
    public void EscapedSurrogatePairAndUtf8NameResolveToSameIdentity()
    {
        var result=Import(Layer(@"A\U+D83D\U+DCA1"),Line("A💡",0,10));
        Assert.Equal("A💡",Assert.Single(result.Project.Pieces).Name);
        Assert.DoesNotContain(result.Warnings,w=>w.Contains("katman görünürlüğü bilinmiyor"));
    }

    [Fact]
    public void DistinctEncodedFormsOfSameLayerRecordAreRejectedAsDuplicate()
        => Assert.Throws<InvalidDataException>(()=>Import(Layer(@"A\U+015F")+Layer("Aş"),Line("Aş",0,10)));

    [Theory]
    [InlineData(@"A\U+12")]
    [InlineData(@"A\U+ZZZZ")]
    [InlineData(@"A\U+D800")]
    [InlineData(@"A\U+DC00")]
    [InlineData(@"A\U+D800\U+0041")]
    [InlineData(@"A\U+000A")]
    [InlineData(@"A\U+005C")]
    [InlineData(@"A\U+0020")]
    [InlineData(@"A\folder")]
    [InlineData(@"A\u+015F")]
    public void MalformedOrAmbiguousLayerEscapesFailClosed(string name)
    {
        Assert.Throws<InvalidDataException>(()=>Import(Layer(name),Line(name,0,10)));
    }

    [Theory]
    [InlineData(" A")]
    [InlineData("A ")]
    [InlineData("\tA")]
    public void RawLayerWhitespaceCannotSilentlyMergeWithAnotherLayer(string ambiguous)
    {
        Assert.Throws<InvalidDataException>(()=>Import(Layer("A"),Line("A",0,10)+Line(ambiguous,10,20)));
        Assert.Throws<InvalidDataException>(()=>Import(Layer(ambiguous),Line("A",0,10)));
    }

    [Theory]
    [InlineData("0\nLINE\n8\nA\n8\nB\n10\n0\n20\n0\n11\n10\n21\n0\n")]
    [InlineData("0\nLWPOLYLINE\n8\nA\n8\nB\n90\n2\n10\n0\n20\n0\n10\n10\n20\n0\n")]
    [InlineData("0\nPOLYLINE\n8\nA\n8\nB\n0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n0\nSEQEND\n")]
    [InlineData("0\nPOLYLINE\n8\nA\n0\nVERTEX\n8\nA\n8\nB\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n0\nSEQEND\n")]
    [InlineData("0\nPOLYLINE\n8\nA\n60\n1\n0\nVERTEX\n8\nA\n8\nB\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n0\nSEQEND\n")]
    public void DuplicateLayerFieldsCannotChooseFirstSilently(string geometry)
        => Assert.Throws<InvalidDataException>(()=>Import(Layer("A")+Layer("B"),geometry));

    [Fact]
    public void NumericGroupsRetainOrdinaryWhitespaceTrimming()
    {
        var line="0\nLINE\n8\nA\n10\n 0 \n20\n 0\n11\n 10\n21\n0 \n";
        var result=Import(Layer("A"),line);
        Assert.Equal(new[]{new Point2(0,0),new Point2(10,0)},Assert.Single(result.Project.Pieces).Points);
    }
}
