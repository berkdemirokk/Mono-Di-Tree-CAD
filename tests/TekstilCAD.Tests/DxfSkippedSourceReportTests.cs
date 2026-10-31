using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfSkippedSourceReportTests
{
    private static DxfImportResult Read(string entities)
    {
        var source="0\nSECTION\n2\nENTITIES\n"+entities+
            "0\nLINE\n8\nCUT\n10\n0\n20\n5\n11\n10\n21\n5\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        return DxfFormat.Import(stream,"report");
    }

    [Fact]
    public void RepeatedSkippedTypesRetainDistinctSourceContext()
    {
        var result=Read("0\nTEXT\n8\nSIZE\n1\nM\n0\nTEXT\n8\nLABEL\n1\nFRONT\n");
        var report=Assert.IsType<DxfSkippedSourceReport>(result.SkippedSourceRecords);
        Assert.False(report.Truncated);
        Assert.Equal(2,report.Records.Length);
        Assert.Equal(new[] { "SIZE", "LABEL" },report.Records.Select(r=>r.Layer));
        Assert.All(report.Records,r=> { Assert.Equal("ENTITIES",r.Section); Assert.Equal("TEXT",r.EntityType); });
        Assert.NotEqual(report.Records[0].SourceOrdinal,report.Records[1].SourceOrdinal);
    }

    [Fact]
    public void LegacyPolylineChildrenAreNotSeparateSkippedRecords()
    {
        var result=Read("0\nPOLYLINE\n0\nVERTEX\n10\n0\n20\n0\n42\n.5\n0\nVERTEX\n10\n10\n20\n0\n0\nSEQEND\n");
        var item=Assert.Single(result.SkippedSourceRecords!.Records);
        Assert.Equal("POLYLINE",item.EntityType);
        Assert.Contains("bulge",item.Reason);
    }

    [Fact]
    public void BoundedReportExplicitlyMarksTruncation()
    {
        var result=Read(string.Concat(Enumerable.Repeat("0\nPOINT\n10\n0\n20\n0\n",4097)));
        Assert.Equal(4096,result.SkippedSourceRecords!.Records.Length);
        Assert.True(result.SkippedSourceRecords.Truncated);
    }

    [Fact]
    public void ManualResultDoesNotClaimInspection()
        => Assert.Null(new DxfImportResult(new ProjectDocument(),[]).SkippedSourceRecords);
}
