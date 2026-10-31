using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfSupplementalGeometryTests
{
    private static string Drawing(string geometry) =>
        "0\nSECTION\n2\nENTITIES\n"+geometry+
        "0\nLINE\n10\n0\n20\n0\n11\n10\n21\n0\n0\nENDSEC\n0\nEOF\n";

    [Fact]
    public void UnsupportedPointsAndCurvesRemainAvailableAsRawSourceRecords()
    {
        var text=Drawing("0\nPOINT\n8\nDRILL\n10\n 2.5 \n20\n4\n"+
            "0\nCIRCLE\n8\nMARK\n10\n3\n20\n5\n40\n0.5\n"+
            "0\nSPLINE\n70\n8\n10\n0\n20\n0\n10\n1\n20\n1\n");
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(text));
        var result=DxfFormat.Import(stream,"supplement");
        var report=result.SupplementalGeometry!;
        Assert.False(report.Truncated);
        Assert.True(report.SkippedPolylinesAssessed);
        Assert.False(report.IncludesUnassessedPolylineCandidates);
        Assert.Equal(new[] {"POINT","CIRCLE","SPLINE"},report.Records.Select(r=>r.EntityType));
        Assert.Contains(report.Records[0].Groups,g=>g.Code==10 && g.RawValue==" 2.5 ");
        Assert.Equal(2,report.Records[2].Groups.Count(g=>g.Code==10));
        Assert.All(report.Records,r=>Assert.Contains(result.SkippedSourceRecords!.Records,s=>s.SourceOrdinal==r.SourceOrdinal));
        Assert.Single(result.Project.Pieces);
    }

    [Fact]
    public void SourceReportSignalsWholeRecordTruncation()
    {
        var text=Drawing(string.Concat(Enumerable.Repeat("0\nPOINT\n10\n2\n20\n3\n",4097)));
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(text));
        var result=DxfFormat.Import(stream,"bounded");
        Assert.Equal(4096,result.SupplementalGeometry!.Records.Length);
        Assert.True(result.SupplementalGeometry.Truncated);
        Assert.All(result.SupplementalGeometry.Records,r=>Assert.Equal(2,r.Groups.Length));
    }

    [Fact]
    public void InspectionReportsUnsupportedGeometryWithoutClaimingItCanBeImported()
    {
        var tables="0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n"+
            "0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n0\nENDTAB\n0\nENDSEC\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(tables+Drawing("0\nPOINT\n10\n2\n20\n3\n")));
        var inspection=DxfFormat.InspectBlockPlacements(stream,DxfTextEncoding.Utf8);
        Assert.Equal("POINT",Assert.Single(inspection.SupplementalGeometry.Records).EntityType);
        Assert.False(inspection.SupplementalGeometry.SkippedPolylinesAssessed);
        Assert.True(inspection.SupplementalGeometry.IncludesUnassessedPolylineCandidates);
    }

    [Fact]
    public void InspectionReportsBothPlainAndCurvedPolylineAsUnassessedCandidates()
    {
        var tables="0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n"+
            "0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n0\nENDTAB\n0\nENDSEC\n";
        var plain="0\nLWPOLYLINE\n90\n2\n10\n0\n20\n0\n10\n1\n20\n0\n";
        var curved="0\nLWPOLYLINE\n90\n2\n10\n2\n20\n0\n42\n0.5\n10\n3\n20\n0\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(tables+Drawing(plain+curved)));
        var report=DxfFormat.InspectBlockPlacements(stream,DxfTextEncoding.Utf8).SupplementalGeometry;
        Assert.True(report.IncludesUnassessedPolylineCandidates);
        Assert.False(report.SkippedPolylinesAssessed);
        Assert.Equal(2,report.Records.Count(r=>r.EntityType=="LWPOLYLINE"));
    }

    [Fact]
    public void ObjectsSectionRecordsDoNotConsumeGeometryInventory()
    {
        var other="0\nSECTION\n2\nOBJECTS\n0\nPOINT\n10\n99\n20\n99\n0\nENDSEC\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(other+Drawing("0\nPOINT\n10\n2\n20\n3\n")));
        var record=Assert.Single(DxfFormat.Import(stream,"scope").SupplementalGeometry!.Records);
        Assert.Equal("ENTITIES",record.Section);
        Assert.Contains(record.Groups,g=>g.Code==10 && g.RawValue=="2");
    }

    [Fact]
    public void SkippedCurvedPolylinesRetainWholeSourceSequences()
    {
        var text=Drawing("0\nLWPOLYLINE\n90\n2\n10\n0\n20\n0\n42\n0.5\n10\n10\n20\n0\n"+
            "0\nPOLYLINE\n70\n2\n0\nVERTEX\n10\n0\n20\n0\n42\n0.25\n"+
            "0\nVERTEX\n10\n10\n20\n0\n0\nSEQEND\n");
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(text));
        var result=DxfFormat.Import(stream,"curves");
        var report=result.SupplementalGeometry!;
        var lightweight=Assert.Single(report.Records,r=>r.EntityType=="LWPOLYLINE");
        Assert.Contains(lightweight.Groups,g=>g.Code==42 && g.RawValue=="0.5");
        var legacy=Assert.Single(report.Records,r=>r.EntityType=="POLYLINE");
        Assert.Equal(new[] {"VERTEX","VERTEX","SEQEND"},legacy.Children.Select(c=>c.EntityType));
        Assert.Contains(legacy.Children[0].Groups,g=>g.Code==42 && g.RawValue=="0.25");
        Assert.All(report.Records,r=>Assert.Contains(result.SkippedSourceRecords!.Records,s=>s.SourceOrdinal==r.SourceOrdinal));
    }

    [Fact]
    public void LegacySequenceIsOmittedAsAWholeAtRecordLimit()
    {
        var points=string.Concat(Enumerable.Repeat("0\nPOINT\n10\n0\n20\n0\n",4095));
        var poly="0\nPOLYLINE\n70\n2\n0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n1\n20\n0\n0\nSEQEND\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(Drawing(points+poly)));
        var report=DxfFormat.Import(stream,"bounded-poly").SupplementalGeometry!;
        Assert.True(report.Truncated);
        Assert.Equal(4095,report.Records.Length);
        Assert.DoesNotContain(report.Records,r=>r.EntityType=="POLYLINE");
    }
    [Fact]
    public void SourceCountsIncludeRecordsBeyondBoundedSampleAndSeparateSections()
    {
        var blocks="0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nB\n"+
            "0\nSPLINE\n70\n8\n0\nPOLYLINE\n70\n2\n0\nVERTEX\n10\n0\n20\n0\n0\nSEQEND\n"+
            "0\nENDBLK\n0\nENDSEC\n";
        var many=string.Concat(Enumerable.Repeat("0\nPOINT\n10\n0\n20\n0\n",4097));
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(blocks+Drawing(many+"0\nSPLINE\n70\n8\n")));
        var report=DxfFormat.Import(stream,"inventory").SupplementalGeometry!;
        Assert.True(report.Truncated);
        Assert.Contains(report.SourceCounts,c=>c.Section=="BLOCKS" && c.EntityType=="SPLINE" && c.Count==1);
        Assert.Contains(report.SourceCounts,c=>c.Section=="BLOCKS" && c.EntityType=="POLYLINE" && c.Count==1);
        Assert.Contains(report.SourceCounts,c=>c.Section=="ENTITIES" && c.EntityType=="POINT" && c.Count==4097);
        Assert.Contains(report.SourceCounts,c=>c.Section=="ENTITIES" && c.EntityType=="SPLINE" && c.Count==1);
    }
}
