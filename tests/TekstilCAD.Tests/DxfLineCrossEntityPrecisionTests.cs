using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfLineCrossEntityPrecisionTests
{
    private static string Line(string start,string end,string layer="0",string color="1") =>
        $"0\nLINE\n8\n{layer}\n6\nCONTINUOUS\n62\n{color}\n10\n{start}\n20\n0\n11\n{end}\n21\n0\n";
    private static string Drawing(string entities) => $"0\nSECTION\n2\nENTITIES\n{entities}0\nENDSEC\n0\nEOF\n";
    private static DxfImportResult Import(string text,double factor=.1)
    {
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(text));
        return DxfFormat.Import(stream,"sentetik",factor);
    }

    [Theory]
    [InlineData("0.1","0.10000000000000002")]
    [InlineData("0.10000000000000002","0.1")]
    public void DifferentSourceEndpointsThatScaleToSamePointAreRejected(string first,string second)
    {
        var bytes=Encoding.UTF8.GetBytes(Drawing(Line("0",first)+Line(second,"1")));
        var before=(byte[])bytes.Clone();
        using var stream=new MemoryStream(bytes,writable:false);
        var error=Assert.Throws<InvalidDataException>(()=>DxfFormat.Import(stream,"sentetik",.1));
        Assert.Contains("ölçekleme hassasiyeti",error.Message);
        Assert.Equal(before,bytes);
    }

    [Fact]
    public void IdenticalSourceEndpointsStillFormAChain()
    {
        var result=Import(Drawing(Line("0","0.1")+Line("0.1","1")));
        Assert.Equal(3,Assert.Single(result.Project.Pieces).Points.Count);
    }

    [Fact]
    public void AliasesInDifferentColorsOrLayersAreNotChainedOrRejected()
    {
        var differentColor=Import(Drawing(Line("0","0.1",color:"1")+Line("0.10000000000000002","1",color:"3")));
        Assert.Equal(2,differentColor.Project.Pieces.Count);
        var differentLayer=Import(Drawing(Line("0","0.1",layer:"A")+Line("0.10000000000000002","1",layer:"B")));
        Assert.Equal(2,differentLayer.Project.Pieces.Count);
    }

    [Fact]
    public void AliasesInDistinctBlockOccurrencesAreNotChainedOrRejected()
    {
        var text="0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n"+Line("0","0.1")+
            "0\nENDBLK\n0\nBLOCK\n2\nA\n"+Line("0.10000000000000002","1")+
            "0\nENDBLK\n0\nENDSEC\n0\nEOF\n";
        Assert.Equal(2,Import(text).Project.Pieces.Count);
    }

    [Fact]
    public void VerySmallDifferentSourceEndpointsCannotSilentlyMergeAfterUnderflow()
    {
        var text=Drawing(Line("-1","1e-300")+Line("2e-300","1"));
        var error=Assert.Throws<InvalidDataException>(()=>Import(text,1e-50));
        Assert.Contains("ölçekleme hassasiyeti",error.Message);
    }
}
