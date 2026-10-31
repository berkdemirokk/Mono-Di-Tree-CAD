using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfMetadataSourceReportTests
{
    private static string Drawing(string entities) =>
        "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n"+
        "0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n0\nENDTAB\n0\nENDSEC\n"+
        "0\nSECTION\n2\nENTITIES\n"+entities+"0\nENDSEC\n0\nEOF\n";

    private const string Line="0\nLINE\n10\n0\n20\n0\n11\n10\n21\n0\n";

    [Fact]
    public void MetadataLocationCanBeRelatedToImportedPieceWithoutParsingWarningText()
    {
        var text=Drawing(Line+"1001\nAPP\n1000\nvalue\n102\n{PRIVATE\n370\n50\n102\n}\n");
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(text));
        var result=DxfFormat.Import(stream,"metadata");
        var record=Assert.Single(result.MetadataSources!.Records);
        Assert.Equal("LINE",record.EntityType);
        Assert.Equal(new DxfMetadataLossSummary(2,1,0,0),record.Counts);
        Assert.Contains(record.SourceOrdinal,Assert.Single(result.PieceSources!.Value).SourceOrdinals);
        Assert.False(result.MetadataSources.Truncated);
    }

    [Fact]
    public void InspectionReportsMetadataEvenWhenControlledImportWouldRejectIt()
    {
        var text=Drawing(Line+"1001\nAPP\n1000\nvalue\n");
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(text));
        var inspection=DxfFormat.InspectBlockPlacements(stream,DxfTextEncoding.Utf8);
        var record=Assert.Single(inspection.MetadataSources.Records);
        Assert.Equal(2,record.Counts.ExtendedDataGroups);
        Assert.Contains(inspection.Limitations,s=>s.Contains("kontrollü aktarım reddedilir"));
    }

    [Fact]
    public void RecordInventorySignalsWhenBounded()
    {
        var points=string.Concat(Enumerable.Repeat("0\nPOINT\n1000\nprivate\n",4097));
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(Drawing(points+Line)));
        var result=DxfFormat.Import(stream,"bounded");
        Assert.Equal(4096,result.MetadataSources!.Records.Length);
        Assert.True(result.MetadataSources.Truncated);
        Assert.Equal(4097,result.MetadataLoss!.ExtendedDataGroups);
    }
}
