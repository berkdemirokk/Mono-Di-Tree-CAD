using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfSourceGeometryInventoryTests
{
    [Fact]
    public void ImportedPieceCanBeAssociatedWithOriginalGroupsAndPrivateData()
    {
        var source="0\nSECTION\n2\nENTITIES\n"+
            "0\nLWPOLYLINE\n5\n ABC \n100\nAcDbPolyline\n8\nCUT\n90\n3\n70\n1\n"+
            "10\n0\n20\n0\n10\n20\n20\n0\n10\n0\n20\n20\n102\n{VENDOR\n1\nprivate\n102\n}\n100\nAfterVendor\n"+
            "0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        var import=DxfFormat.Import(stream,"source geometry");
        Assert.Contains(import.Warnings,w=>w.Contains("tanıtıcıları") && w.Contains("korunmaz"));
        var piece=Assert.Single(import.Project.Pieces);
        var provenance=Assert.Single(import.PieceSources!.Value);
        Assert.Equal(piece.Id,provenance.PieceId);
        var record=Assert.Single(import.SourceGeometry!.Records);
        Assert.Contains(record.SourceOrdinal,provenance.SourceOrdinals);
        Assert.Contains(record.Groups,g=>g.Code==5 && g.RawValue==" ABC ");
        Assert.Contains(record.Groups,g=>g.Code==100 && g.RawValue=="AcDbPolyline");
        Assert.DoesNotContain(record.Groups,g=>g.RawValue=="private");
        Assert.Contains(import.OpaqueMetadata!.Groups,g=>g.SourceOrdinal==record.SourceOrdinal && g.RawValue=="private");
        Assert.Equal(record.Groups.Length,record.GroupOrdinals.Length);
        Assert.Equal<int>(record.GroupOrdinals,record.Groups.Select(g=>g.GroupOrdinal));
        var privateGroup=Assert.Single(import.OpaqueMetadata.Groups,g=>g.RawValue=="private");
        var afterIndex=record.Groups.Select((group,index)=>(group,index)).Single(pair=>pair.group.RawValue=="AfterVendor").index;
        Assert.True(record.GroupOrdinals[afterIndex]>privateGroup.GroupOrdinal);
        Assert.DoesNotContain(privateGroup.GroupOrdinal,record.GroupOrdinals);
        var token=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var plan=DxfTextileLayerRolePlan.Create(import,[new("CUT",DxfTextileRole.Cutting)],token);
        Assert.Same(import.SourceGeometry,plan.SourceGeometry);
        Assert.False(plan.SourceGeometry!.Truncated);
    }

    [Fact]
    public void OversizedRecordIsOmittedWholeAndReportedAsTruncated()
    {
        var source="0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nCUT\n10\n0\n20\n0\n11\n1\n21\n1\n"+
            string.Concat(Enumerable.Repeat("100\nX\n",32768))+"0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        var import=DxfFormat.Import(stream,"bounded inventory");
        Assert.True(import.SourceGeometry!.Truncated);
        Assert.Empty(import.SourceGeometry.Records);
    }

    [Fact]
    public void InspectionRetainsGeometryEvidenceWhenControlledImportRejectsPrivateData()
    {
        var source="0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nCUT\n5\nABC\n10\n0\n20\n0\n11\n1\n21\n1\n"+
            "102\n{VENDOR\n1\nprivate\n102\n}\n0\nENDSEC\n0\nEOF\n";
        var bytes=Encoding.UTF8.GetBytes(source);
        using var stream=new MemoryStream(bytes,writable:false);
        var inspection=DxfFormat.InspectBlockPlacements(stream,DxfTextEncoding.Utf8);
        var record=Assert.Single(inspection.SourceGeometry!.Records);
        Assert.Equal("LINE",record.EntityType);
        Assert.Contains(record.Groups,g=>g.Code==5 && g.RawValue=="ABC");
        Assert.Contains(inspection.OpaqueMetadata.Groups,g=>g.SourceOrdinal==record.SourceOrdinal && g.RawValue=="private");
        Assert.Equal(0,stream.Position);
        Assert.Throws<InvalidDataException>(()=>DxfFormat.ImportWithInspectedBlockPlacements(stream,"rejected",1,0,["CUT"],inspection));
        Assert.Equal(bytes,stream.ToArray());
    }

    [Fact]
    public void InspectionRetainsUninterpretedInsertTransformGroups()
    {
        var source="0\nSECTION\n2\nENTITIES\n0\nINSERT\n2\nPART\n8\nCUT\n10\n 12.500 \n20\n3\n41\n2\n50\n90\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        var inspection=DxfFormat.InspectBlockPlacements(stream,DxfTextEncoding.Utf8);
        var record=Assert.Single(inspection.SourceGeometry!.Records);
        Assert.Equal("INSERT",record.EntityType);
        Assert.Contains(record.Groups,g=>g.Code==10 && g.RawValue==" 12.500 ");
        Assert.Contains(record.Groups,g=>g.Code==41 && g.RawValue=="2");
        Assert.Contains(record.Groups,g=>g.Code==50 && g.RawValue=="90");
    }

    [Fact]
    public void InspectionWarnsWhenGeometryReferencesAreNotPersisted()
    {
        var source="0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nCUT\n330\nAB\n10\n0\n20\n0\n11\n1\n21\n1\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        var inspection=DxfFormat.InspectBlockPlacements(stream,DxfTextEncoding.Utf8);
        Assert.Contains(inspection.Warnings,w=>w.Contains("bağlantıları") && w.Contains("korunmaz"));
        Assert.Contains(Assert.Single(inspection.SourceGeometry!.Records).Groups,g=>g.Code==330 && g.RawValue=="AB");
    }

    [Fact]
    public void AnnotationAndPointGroupsRetainPositionsAroundPrivateGroups()
    {
        var source="0\nSECTION\n2\nENTITIES\n"+
            "0\nTEXT\n8\nLABEL\n102\n{APP\n1\nprivate text\n102\n}\n1\nSize M\n"+
            "0\nPOINT\n8\nMARK\n102\n{APP\n1\nprivate point\n102\n}\n10\n2\n20\n3\n"+
            "0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        var inspection=DxfFormat.InspectBlockPlacements(stream,DxfTextEncoding.Utf8);
        var annotation=Assert.Single(inspection.Annotations.Records);
        var point=Assert.Single(inspection.SupplementalGeometry.Records);
        var privateText=Assert.Single(inspection.OpaqueMetadata.Groups,g=>g.RawValue=="private text");
        var privatePoint=Assert.Single(inspection.OpaqueMetadata.Groups,g=>g.RawValue=="private point");
        Assert.Equal(annotation.SourceOrdinal,privateText.SourceOrdinal);
        Assert.Equal(point.SourceOrdinal,privatePoint.SourceOrdinal);
        Assert.True(Assert.Single(annotation.Groups,g=>g.RawValue=="Size M").GroupOrdinal>privateText.GroupOrdinal);
        Assert.True(Assert.Single(point.Groups,g=>g.Code==10).GroupOrdinal>privatePoint.GroupOrdinal);
    }
}
