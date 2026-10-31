using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfContinuousDefinitionSafetyTests
{
    private static MemoryStream Drawing(string definition) => new(Encoding.UTF8.GetBytes(
        "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLTYPE\n" + definition +
        "0\nENDTAB\n0\nTABLE\n2\nLAYER\n0\nLAYER\n2\nCUT\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n" +
        "0\nENDTAB\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n" +
        "0\nLWPOLYLINE\n8\nCUT\n70\n1\n90\n3\n10\n0\n20\n0\n10\n10\n20\n0\n10\n0\n20\n10\n" +
        "0\nENDSEC\n0\nEOF\n"));

    [Theory]
    [InlineData("73\n2\n40\n2\n49\n1\n49\n-1\n")]
    [InlineData("73\n0\n40\n0\n49\n0\n")]
    [InlineData("73\n0\n40\n1e-400\n")]
    [InlineData("73\n0\n73\n2\n40\n0\n")]
    public void PatternedOrAmbiguousContinuousDefinitionRejectsAllImportModes(string fields)
    {
        var definition="0\nLTYPE\n2\ncontinuous\n"+fields;
        using var general=Drawing(definition);
        Assert.Throws<InvalidDataException>(()=>DxfFormat.Import(general,"test"));
        using var controlled=Drawing(definition);
        Assert.Throws<InvalidDataException>(()=>DxfFormat.ImportWithBlockPlacements(controlled,"test",1,DxfTextEncoding.Utf8,0,["CUT"]));
        using var inspection=Drawing(definition);
        Assert.Throws<InvalidDataException>(()=>DxfFormat.InspectBlockPlacements(inspection,DxfTextEncoding.Utf8));
    }

    [Fact]
    public void DeclaredSolidContinuousStillImports()
    {
        using var input=Drawing("0\nLTYPE\n2\nCONTINUOUS\n70\n0\n72\n65\n73\n0\n40\n0\n");
        Assert.Single(DxfFormat.Import(input,"test").Project.Pieces);
    }
}
