using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfSourceGeometryViewTests
{
    [Fact]
    public void JoinsPrivateAndExtendedGroupsInSourceOrderWithoutDuplicatingXdata()
    {
        var source="0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nCUT\n10\n0\n20\n0\n"+
            "102\n{APP\n1\nprivate\n102\n}\n11\n1\n21\n1\n1001\nVENDOR\n1000\nextra\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        var import=DxfFormat.Import(stream,"view");
        var ordinal=Assert.Single(import.SourceGeometry!.Records).SourceOrdinal;
        var view=DxfSourceGeometryView.Read(import,ordinal);
        Assert.Equal(import.SourceSha256,view.SourceSha256);
        Assert.Equal(import.SourceByteLength,view.SourceByteLength);
        Assert.Equal(DxfTextEncoding.Utf8,view.Encoding);
        Assert.Equal(Enumerable.Range(1,view.Groups.Length),view.Groups.Select(g=>g.GroupOrdinal));
        Assert.True(Assert.Single(view.Groups,g=>g.RawValue=="private").InApplicationGroup);
        Assert.False(Assert.Single(view.Groups,g=>g.RawValue=="extra").InApplicationGroup);
        Assert.Single(view.Groups,g=>g.RawValue=="VENDOR");
        Assert.Throws<InvalidDataException>(()=>DxfSourceGeometryView.Read(import with { SourceSha256=new string('0',64) },ordinal));
    }

    [Fact]
    public void TruncatedPrivateInventoryCannotBePresentedAsCompleteRecord()
    {
        var source="0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nCUT\n10\n0\n20\n0\n11\n1\n21\n1\n102\n{APP\n"+
            string.Concat(Enumerable.Repeat("1\nX\n",32768))+"102\n}\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        var import=DxfFormat.Import(stream,"truncated");
        Assert.True(import.OpaqueMetadata!.Truncated);
        var ordinal=Assert.Single(import.SourceGeometry!.Records).SourceOrdinal;
        Assert.Throws<InvalidDataException>(()=>DxfSourceGeometryView.Read(import,ordinal));
    }

    [Fact]
    public void InspectionJoinsTextAndPointRecordsWithPrivateGroups()
    {
        var source="0\nSECTION\n2\nENTITIES\n"+
            "0\nTEXT\n8\nLABEL\n102\n{APP\n1\ntext-private\n102\n}\n1\nM\n"+
            "0\nPOINT\n8\nMARK\n102\n{APP\n1\npoint-private\n102\n}\n10\n2\n20\n3\n"+
            "0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        var inspection=DxfFormat.InspectBlockPlacements(stream,DxfTextEncoding.Utf8);
        var annotation=Assert.Single(inspection.Annotations.Records);
        var point=Assert.Single(inspection.SupplementalGeometry.Records);
        var textView=DxfSourceGeometryView.ReadAnnotation(inspection,annotation.SourceOrdinal);
        var pointView=DxfSourceGeometryView.ReadSupplemental(inspection,point.SourceOrdinal);
        Assert.True(Assert.Single(textView.Groups,g=>g.RawValue=="text-private").InApplicationGroup);
        Assert.True(Assert.Single(pointView.Groups,g=>g.RawValue=="point-private").InApplicationGroup);
        Assert.Equal(Enumerable.Range(1,textView.Groups.Length),textView.Groups.Select(g=>g.GroupOrdinal));
        Assert.Equal(Enumerable.Range(1,pointView.Groups.Length),pointView.Groups.Select(g=>g.GroupOrdinal));
        Assert.Throws<InvalidDataException>(()=>DxfSourceGeometryView.ReadAnnotation(inspection,point.SourceOrdinal));
    }

    [Fact]
    public void UnsupportedVendorRecordRetainsOrdinaryAndPrivateGroups()
    {
        var source="0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nCUT\n10\n0\n20\n0\n11\n1\n21\n1\n0\nVENDOR_MARK\n8\nMARK\n1\n  raw label  \n"+
            "102\n{APP\n1\nprivate\n102\n}\n90\n42\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        var import=DxfFormat.Import(stream,"unknown");
        var detail=Assert.Single(import.SkippedSourceDetails!.Records,r=>r.Source.EntityType=="VENDOR_MARK");
        var view=DxfSourceGeometryView.ReadSkipped(import,detail.Source.SourceOrdinal);
        Assert.Contains(view.Groups,g=>g.RawValue=="  raw label  ");
        Assert.True(Assert.Single(view.Groups,g=>g.RawValue=="private").InApplicationGroup);
        Assert.Contains(view.Groups,g=>g.Code==90 && g.RawValue=="42");
        Assert.False(import.SkippedSourceDetails.Truncated);
    }

    [Fact]
    public void InspectionRetainsUnassessedVendorRecordBeforeAnyImport()
    {
        var source="0\nSECTION\n2\nENTITIES\n0\nVENDOR_MARK\n8\nMARK\n1\n  raw  \n"+
            "102\n{APP\n1\nprivate\n102\n}\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        var inspection=DxfFormat.InspectBlockPlacements(stream,DxfTextEncoding.Utf8);
        var item=Assert.Single(inspection.OtherSourceRecords!.Records);
        Assert.Equal("VENDOR_MARK",item.EntityType);
        var view=DxfSourceGeometryView.ReadOther(inspection,item.SourceOrdinal);
        Assert.Contains(view.Groups,g=>g.RawValue=="  raw  ");
        Assert.True(Assert.Single(view.Groups,g=>g.RawValue=="private").InApplicationGroup);
    }

    [Fact]
    public void ObjectDictionaryIsRetainedForInspectionAndImportWithoutResolvingReferences()
    {
        var source="0\nSECTION\n2\nOBJECTS\n0\nDICTIONARY\n5\nAB\n3\nVENDOR\n350\nCD\n"+
            "102\n{APP\n1\nprivate\n102\n}\n0\nENDSEC\n"+
            "0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nCUT\n10\n0\n20\n0\n11\n1\n21\n1\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        var inspection=DxfFormat.InspectBlockPlacements(stream,DxfTextEncoding.Utf8);
        var record=Assert.Single(inspection.SourceObjects!.Records);
        Assert.Equal("OBJECTS",record.Section);
        Assert.Contains(inspection.Warnings,w=>w.Contains("OBJECTS") && w.Contains("aktarılmaz"));
        var inspected=DxfSourceGeometryView.ReadObject(inspection,record.SourceOrdinal);
        Assert.Equal(inspection.SourceSha256,inspected.SourceSha256);
        Assert.Equal(inspection.SourceByteLength,inspected.SourceByteLength);
        Assert.Contains(inspected.Groups,g=>g.Code==350 && g.RawValue=="CD");
        Assert.True(Assert.Single(inspected.Groups,g=>g.RawValue=="private").InApplicationGroup);
        var import=DxfFormat.Import(stream,"objects");
        Assert.Contains(import.Warnings,w=>w.Contains("OBJECTS") && w.Contains("aktarılmaz"));
        var imported=DxfSourceGeometryView.ReadObject(import,Assert.Single(import.SourceObjects!.Records).SourceOrdinal);
        Assert.Equal<DxfSourceGroupView>(inspected.Groups,imported.Groups);
    }

    [Fact]
    public void SameOrdinalInDifferentSnapshotsRetainsDistinctSourceIdentity()
    {
        static DxfImportResult Read(string x)
        {
            var source="0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nCUT\n10\n0\n20\n0\n11\n"+x+"\n21\n1\n0\nENDSEC\n0\nEOF\n";
            using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
            return DxfFormat.Import(stream,"identity");
        }
        var first=Read("1");var second=Read("2");
        var one=DxfSourceGeometryView.Read(first,Assert.Single(first.SourceGeometry!.Records).SourceOrdinal);
        var two=DxfSourceGeometryView.Read(second,Assert.Single(second.SourceGeometry!.Records).SourceOrdinal);
        Assert.Equal(one.SourceOrdinal,two.SourceOrdinal);
        Assert.Equal(one.SourceByteLength,two.SourceByteLength);
        Assert.NotEqual(one.SourceSha256,two.SourceSha256);
    }

    [Fact]
    public void RecordViewCanVerifyAndRecaptureEntireOriginalSource()
    {
        var source="0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nCUT\n10\n0\n20\n0\n11\n1\n21\n1\n0\nENDSEC\n0\nEOF\n";
        var bytes=Encoding.UTF8.GetBytes(source);
        using var stream=new MemoryStream(bytes,writable:false);
        var inspection=DxfFormat.InspectBlockPlacements(stream,DxfTextEncoding.Utf8);
        var view=DxfSourceGeometryView.Read(inspection,Assert.Single(inspection.SourceGeometry!.Records).SourceOrdinal);
        Assert.Equal(bytes,DxfVerifiedSourceSnapshot.Capture(stream,view));
        var changed=Encoding.UTF8.GetBytes(source.Replace("11\n1\n","11\n2\n"));
        using var different=new MemoryStream(changed,writable:false);
        Assert.Throws<InvalidDataException>(()=>DxfVerifiedSourceSnapshot.Capture(different,view));
        var unbound=new DxfSourceRecordView(view.SourceOrdinal,view.Section,view.Block,view.EntityType,view.Groups);
        using var original=new MemoryStream(bytes,writable:false);
        Assert.Throws<InvalidDataException>(()=>DxfVerifiedSourceSnapshot.Capture(original,unbound));
    }

    [Fact]
    public void SourceBoundRecordContentHasNoPublicOrPrivateSetter()
    {
        foreach(var name in new[] { nameof(DxfSourceRecordView.SourceOrdinal),nameof(DxfSourceRecordView.Section),
            nameof(DxfSourceRecordView.Block),nameof(DxfSourceRecordView.EntityType),nameof(DxfSourceRecordView.Groups) })
            Assert.Null(typeof(DxfSourceRecordView).GetProperty(name)!.GetSetMethod(nonPublic:true));
        Assert.True(typeof(DxfSourceRecordView).IsSealed);
    }

    [Fact]
    public void SameBytesRetainTheExplicitDecoderInTheirRecordViews()
    {
        var source="0\nSECTION\n2\nENTITIES\n0\nTEXT\n8\nLABEL\n1\nÖ\n0\nENDSEC\n0\nEOF\n";
        var bytes=Encoding.UTF8.GetBytes(source);
        using var first=new MemoryStream(bytes,writable:false);
        using var second=new MemoryStream(bytes,writable:false);
        var utf8=DxfFormat.InspectBlockPlacements(first,DxfTextEncoding.Utf8);
        var ansi=DxfFormat.InspectBlockPlacements(second,DxfTextEncoding.Windows1254);
        var a=DxfSourceGeometryView.ReadAnnotation(utf8,Assert.Single(utf8.Annotations.Records).SourceOrdinal);
        var b=DxfSourceGeometryView.ReadAnnotation(ansi,Assert.Single(ansi.Annotations.Records).SourceOrdinal);
        Assert.Equal(a.SourceSha256,b.SourceSha256);
        Assert.Equal(DxfTextEncoding.Utf8,a.Encoding);
        Assert.Equal(DxfTextEncoding.Windows1254,b.Encoding);
        Assert.Equal("Ö",Assert.Single(a.Groups,g=>g.Code==1).RawValue);
        Assert.NotEqual(Assert.Single(a.Groups,g=>g.Code==1).RawValue,Assert.Single(b.Groups,g=>g.Code==1).RawValue);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ObjectLossWarningDistinguishesDictionaryFromEmptySection(bool includeDictionary)
    {
        var source="0\nSECTION\n2\nOBJECTS\n"+(includeDictionary ? "0\nDICTIONARY\n5\nAB\n3\nVENDOR\n350\nCD\n" : "")+
            "0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nCUT\n10\n0\n20\n0\n11\n1\n21\n1\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        var inspection=DxfFormat.InspectBlockPlacements(stream,DxfTextEncoding.Utf8);
        var import=DxfFormat.Import(stream,"objects warning");
        Assert.Equal(includeDictionary,inspection.Warnings.Any(w=>w.Contains("OBJECTS") && w.Contains("aktarılmaz")));
        Assert.Equal(includeDictionary,import.Warnings.Any(w=>w.Contains("OBJECTS") && w.Contains("aktarılmaz")));
    }

    [Fact]
    public void SkippedDetailLimitKeepsEarlierRecordsWholeAndReportsTruncation()
    {
        var source="0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nCUT\n10\n0\n20\n0\n11\n1\n21\n1\n"+
            "0\nVENDOR_A\n8\nMARK\n1\nearlier\n0\nVENDOR_B\n8\nMARK\n"+
            string.Concat(Enumerable.Repeat("100\nX\n",32768))+"0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        var import=DxfFormat.Import(stream,"bounded skipped details");
        Assert.True(import.SkippedSourceDetails!.Truncated);
        Assert.Contains(import.Warnings,w=>w.Contains("envanteri kesildi"));
        var first=Assert.Single(import.SkippedSourceDetails.Records);
        Assert.Equal("VENDOR_A",first.Source.EntityType);
        var view=DxfSourceGeometryView.ReadSkipped(import,first.Source.SourceOrdinal);
        Assert.Contains(view.Groups,g=>g.RawValue=="earlier");
        Assert.Contains(import.SkippedSourceRecords!.Records,r=>r.EntityType=="VENDOR_B");
    }
}
