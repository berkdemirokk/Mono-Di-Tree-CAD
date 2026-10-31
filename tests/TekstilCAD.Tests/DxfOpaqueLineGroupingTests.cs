using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfOpaqueLineGroupingTests
{
    private static DxfImportResult Read(string firstMetadata,string secondMetadata,string prefix="")
    {
        var source="0\nSECTION\n2\nENTITIES\n"+prefix+
            "0\nLINE\n8\nCUT\n6\nCONTINUOUS\n10\n0\n20\n0\n11\n10\n21\n0\n"+firstMetadata+
            "0\nLINE\n8\nCUT\n6\nCONTINUOUS\n10\n10\n20\n0\n11\n20\n21\n0\n"+secondMetadata+
            "0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        return DxfFormat.Import(stream,"opaque");
    }

    [Theory]
    [InlineData("1001\nAPP\n1000\nCUT\n","")]
    [InlineData("102\n{APP\n1\nCUT\n102\n}\n","")]
    [InlineData("1001\nAPP\n1000\nCUT\n","1001\nAPP\n1000\nCUT\n")]
    public void UninterpretedMetadataLinesRemainSeparate(string first,string second)
    {
        var result=Read(first,second);
        Assert.Equal(2,result.Project.Pieces.Count);
        Assert.Contains(result.Warnings,w=>w.Contains("LINE kayıtları anlamları bilinmediği için ayrı aktarıldı"));
        Assert.All(result.PieceSources!.Value,s=>Assert.Single(s.SourceOrdinals));
    }

    [Fact]
    public void OrdinaryConnectedLinesStillJoin()
        => Assert.Single(Read("","").Project.Pieces);

    [Fact]
    public void SeparationDoesNotDependOnTruncatedMetadataReport()
    {
        var prefix=string.Concat(Enumerable.Repeat("0\nPOINT\n1000\nprivate\n",4097));
        var result=Read("1001\nAPP\n1000\nCUT\n","",prefix);
        Assert.True(result.MetadataSources!.Truncated);
        Assert.Equal(2,result.Project.Pieces.Count);
    }
}
