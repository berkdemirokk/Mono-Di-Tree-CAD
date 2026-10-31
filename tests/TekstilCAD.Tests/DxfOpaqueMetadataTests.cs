using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfOpaqueMetadataTests
{
    private static string Drawing(string metadata) =>
        "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n"+
        "0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n0\nENDTAB\n0\nENDSEC\n"+
        "0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n10\n21\n0\n"+metadata+
        "0\nENDSEC\n0\nEOF\n";

    [Fact]
    public void CapturesDecodedRawValuesAndPrivateBoundariesInSourceOrder()
    {
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(Drawing("1001\nAPP\n1000\n  value  \n102\n{APP\n1000\nprivate\n102\n}\n")));
        var result=DxfFormat.Import(stream,"opaque");
        var captured=result.OpaqueMetadata!;
        Assert.False(captured.Truncated);
        Assert.Equal(new[] {1001,1000,102,1000,102},captured.Groups.Select(g=>g.Code));
        Assert.Equal("  value  ",captured.Groups[1].RawValue);
        Assert.Equal(new[] {false,false,true,true,true},captured.Groups.Select(g=>g.InApplicationGroup));
        Assert.True(captured.Groups.Zip(captured.Groups.Skip(1),(a,b)=>a.GroupOrdinal<b.GroupOrdinal).All(x=>x));
        Assert.All(captured.Groups,g=>Assert.Contains(g.SourceOrdinal,Assert.Single(result.PieceSources!.Value).SourceOrdinals));
    }

    [Fact]
    public void InspectionCapturesOpaqueDataBeforeControlledImportRejection()
    {
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(Drawing("1001\nAPP\n1000\ntext\n")));
        var inspection=DxfFormat.InspectBlockPlacements(stream,DxfTextEncoding.Utf8);
        Assert.Equal(2,inspection.OpaqueMetadata.Groups.Length);
        Assert.False(inspection.OpaqueMetadata.Truncated);
    }

    [Fact]
    public void GroupLimitReturnsPrefixAndReportsTruncation()
    {
        var metadata=string.Concat(Enumerable.Repeat("1000\nx\n",32769));
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(Drawing(metadata)));
        var result=DxfFormat.Import(stream,"bounded");
        Assert.Equal(32768,result.OpaqueMetadata!.Groups.Length);
        Assert.True(result.OpaqueMetadata.Truncated);
        Assert.Equal(32769,result.MetadataLoss!.ExtendedDataGroups);
    }
}
