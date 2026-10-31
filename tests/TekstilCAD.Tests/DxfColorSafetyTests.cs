using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfColorSafetyTests
{
    private static DxfImportResult Import(string entities,string tables="")
    {
        var text=$"0\nSECTION\n2\nTABLES\n{tables}0\nENDSEC\n0\nSECTION\n2\nENTITIES\n{entities}0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(text));
        return DxfFormat.Import(stream,"sentetik");
    }
    private static string Line(int x1,int x2,string color="") =>
        $"0\nLINE\n8\n0\n6\nCONTINUOUS\n10\n{x1}\n20\n0\n11\n{x2}\n21\n0\n{color}";
    private static string Layer(string color) => $"0\nTABLE\n2\nLAYER\n0\nLAYER\n2\n0\n70\n0\n{color}6\nCONTINUOUS\n0\nENDTAB\n";

    [Fact]
    public void DifferentExplicitAciColorsDoNotBecomeOneContour()
    {
        var result=Import(Line(0,10,"62\n1\n")+Line(10,20,"62\n3\n"));
        Assert.Equal(2,result.Project.Pieces.Count);
        Assert.Contains(result.Warnings,w=>w.Contains("renk bilgileri projede korunmaz"));
    }
    [Fact]
    public void SameExplicitColorsCanStillJoin()
    {
        var result=Import(Line(0,10,"62\n1\n")+Line(10,20,"62\n1\n"));
        Assert.Single(result.Project.Pieces);
        Assert.DoesNotContain(result.Warnings,w=>w.Contains("renk mirası doğrulanamadı"));
    }
    [Fact]
    public void KnownByLayerColorCanJoinMatchingExplicitColor()
    {
        var result=Import(Line(0,10)+Line(10,20,"62\n3\n"),Layer("62\n3\n"));
        Assert.Single(result.Project.Pieces);
        Assert.DoesNotContain(result.Warnings,w=>w.Contains("renk mirası doğrulanamadı"));
    }
    [Fact]
    public void UnknownInheritanceDoesNotJoinExplicitOrOtherInheritance()
    {
        var result=Import(Line(0,10,"62\n7\n")+Line(10,20)+Line(20,30,"62\n0\n"));
        Assert.Equal(3,result.Project.Pieces.Count);
        Assert.Contains(result.Warnings,w=>w.Contains("renk mirası doğrulanamadı"));
    }
    [Fact]
    public void TrueColorOverridesAciAndDifferentTrueColorsRemainSeparate()
    {
        var result=Import(Line(0,10,"62\n1\n420\n255\n")+Line(10,20,"62\n3\n420\n255\n")+Line(20,30,"62\n1\n420\n65280\n"));
        Assert.Equal(2,result.Project.Pieces.Count);
    }
    [Fact]
    public void LayerTrueColorOverridesLayerAci()
    {
        var result=Import(Line(0,10)+Line(10,20,"62\n1\n")+Line(20,30,"420\n255\n"),Layer("62\n1\n420\n255\n"));
        Assert.Equal(3,result.Project.Pieces.Count);
    }
    [Theory]
    [InlineData("62\n1\n62\n2\n")]
    [InlineData("62\n-1\n")]
    [InlineData("62\n257\n")]
    [InlineData("420\n16777216\n")]
    [InlineData("420\n1\n420\n2\n")]
    public void InvalidOrRepeatedEntityColorFailsRatherThanGuessing(string color)
    {
        Assert.Throws<InvalidDataException>(()=>Import(Line(0,10,color)));
    }
    [Fact]
    public void DifferentNamedColorsAreNotCombinedAndRemainUnverified()
    {
        var result=Import(Line(0,10,"430\nbook$red\n")+Line(10,20,"430\nbook$blue\n"));
        Assert.Equal(2,result.Project.Pieces.Count);
        Assert.Contains(result.Warnings,w=>w.Contains("renk mirası doğrulanamadı"));
    }
}
