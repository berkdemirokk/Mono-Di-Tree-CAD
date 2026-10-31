using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfAnnotationReportTests
{
    private static string Drawing(string annotations) =>
        "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n"+
        "0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n0\nENDTAB\n0\nENDSEC\n"+
        "0\nSECTION\n2\nENTITIES\n"+annotations+
        "0\nLINE\n10\n0\n20\n0\n11\n10\n21\n0\n0\nENDSEC\n0\nEOF\n";

    [Fact]
    public void TextAndMtextGroupsSurviveWithoutGuessingTheirMeaning()
    {
        var text=Drawing("0\nTEXT\n8\nLABEL\n10\n3\n20\n4\n1\n  Size M  \n"+
            "0\nMTEXT\n8\nLABEL\n3\nfirst\n3\nsecond\n1\nlast\n");
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(text));
        var result=DxfFormat.Import(stream,"annotations");
        var report=result.Annotations!;
        Assert.False(report.Truncated);
        var first=Assert.Single(report.Records,r=>r.EntityType=="TEXT");
        Assert.Equal("ENTITIES",first.Section);
        Assert.Contains(first.Groups,g=>g.Code==1 && g.RawValue=="  Size M  ");
        Assert.Contains(first.Groups,g=>g.Code==10 && g.RawValue=="3");
        var multi=Assert.Single(report.Records,r=>r.EntityType=="MTEXT");
        Assert.Equal(new[] {"first","second","last"},multi.Groups.Where(g=>g.Code is 1 or 3).Select(g=>g.RawValue));
        Assert.Contains(result.SkippedSourceRecords!.Records,r=>r.SourceOrdinal==first.SourceOrdinal);
    }

    [Fact]
    public void InspectionFindsAttributesWithoutClaimingPlacement()
    {
        var text=Drawing("0\nATTDEF\n2\nSIZE\n1\nM\n");
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(text));
        var inspection=DxfFormat.InspectBlockPlacements(stream,DxfTextEncoding.Utf8);
        var record=Assert.Single(inspection.Annotations.Records);
        Assert.Equal("ATTDEF",record.EntityType);
        Assert.Contains(record.Groups,g=>g.Code==2 && g.RawValue=="SIZE");
    }

    [Fact]
    public void LimitOmitsWholeRecordsAndSignalsTruncation()
    {
        var text=Drawing(string.Concat(Enumerable.Repeat("0\nTEXT\n1\nM\n",4097)));
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(text));
        var report=DxfFormat.Import(stream,"bounded").Annotations!;
        Assert.Equal(4096,report.Records.Length);
        Assert.True(report.Truncated);
        Assert.All(report.Records,r=>Assert.Single(r.Groups));
    }
}
