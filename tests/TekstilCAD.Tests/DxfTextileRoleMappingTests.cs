using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfTextileRoleMappingTests
{
    private static DxfImportResult Read()
    {
        var source="0\nSECTION\n2\nENTITIES\n"+
            "0\nLWPOLYLINE\n8\nCUT\n90\n3\n70\n1\n10\n0\n20\n0\n10\n20\n20\n0\n10\n0\n20\n20\n"+
            "0\nLINE\n8\nDETAIL\n10\n2\n20\n2\n11\n2\n21\n8\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        return DxfFormat.Import(stream,"roles");
    }

    [Fact]
    public void ExplicitRolesApplyToCopyWithoutInferringLayers()
    {
        var import=Read();var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var detail=Assert.Single(import.Project.Pieces,p=>!p.Closed);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var result=DxfTextileRoleMapping.Apply(import,[new(contour.Id,DxfTextileRole.Cutting),new(detail.Id,DxfTextileRole.Auxiliary)],fingerprint);
        Assert.Equal("Kesim",Assert.Single(result.Project.Pieces,p=>p.Id==contour.Id).Metadata!.Role);
        Assert.Equal("Yardımcı",Assert.Single(result.Project.Pieces,p=>p.Id==detail.Id).Metadata!.Role);
        Assert.Equal(2,result.Assignments.Length);Assert.Null(contour.Metadata);Assert.Null(detail.Metadata);
    }

    [Fact]
    public void OpenCuttingRoleAndDuplicateSelectionsAreRejectedWithoutMutation()
    {
        var import=Read();var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var detail=Assert.Single(import.Project.Pieces,p=>!p.Closed);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        Assert.Throws<InvalidDataException>(()=>DxfTextileRoleMapping.Apply(import,[new(detail.Id,DxfTextileRole.Cutting)],fingerprint));
        Assert.Throws<InvalidDataException>(()=>DxfTextileRoleMapping.Apply(import,[new(contour.Id,DxfTextileRole.Cutting),new(contour.Id,DxfTextileRole.Auxiliary)],fingerprint));
        Assert.Null(contour.Metadata);Assert.Null(detail.Metadata);
    }

    [Fact]
    public void ChangedProjectInvalidatesRoleSelection()
    {
        var import=Read();var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        contour.Name="edited";
        Assert.Throws<InvalidDataException>(()=>DxfTextileRoleMapping.Apply(import,[new(contour.Id,DxfTextileRole.Cutting)],fingerprint));
        Assert.Null(contour.Metadata);
    }
}
