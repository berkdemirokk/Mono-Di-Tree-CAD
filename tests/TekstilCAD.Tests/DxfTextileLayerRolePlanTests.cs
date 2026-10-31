using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfTextileLayerRolePlanTests
{
    private static DxfImportResult Read()
    {
        var source="0\nSECTION\n2\nENTITIES\n"+
            "0\nLWPOLYLINE\n8\nCUT\n90\n3\n70\n1\n10\n0\n20\n0\n10\n20\n20\n0\n10\n0\n20\n20\n"+
            "0\nLINE\n8\nCUT\n10\n2\n20\n2\n11\n2\n21\n8\n"+
            "0\nPOINT\n8\nCUT\n10\n5\n20\n5\n"+
            "0\nTEXT\n8\nCUT\n10\n3\n20\n3\n1\nSize M\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        return DxfFormat.Import(stream,"layer review");
    }

    [Fact]
    public void MixedLayerListsEveryPieceAndPreservesSkippedRecords()
    {
        var import=Read();
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var plan=DxfTextileLayerRolePlan.Create(import,[new("CUT",DxfTextileRole.Cutting)],fingerprint);
        Assert.Equal(2,plan.Candidates.Length);
        var closed=Assert.Single(plan.Candidates,c=>c.Closed);
        var open=Assert.Single(plan.Candidates,c=>!c.Closed);
        Assert.Equal(DxfLayerRoleEligibility.Eligible,closed.Eligibility);
        Assert.Equal(DxfLayerRoleEligibility.OpenCuttingContour,open.Eligibility);
        Assert.All(plan.Candidates,c=>Assert.Equal("CUT",c.Source.Layer));
        Assert.Contains(plan.SkippedSourceRecords!.Records,r=>r.EntityType=="POINT");
        Assert.Contains(plan.SupplementalGeometry!.Records,r=>r.EntityType=="POINT");
        Assert.Contains(plan.Annotations!.Records,r=>r.EntityType=="TEXT");
        Assert.Throws<InvalidDataException>(()=>DxfTextileRoleMapping.Apply(import,plan.Candidates.Select(c=>c.Selection).ToArray(),fingerprint));
        var mapped=DxfTextileRoleMapping.Apply(import,[closed.Selection],fingerprint);
        Assert.Equal("Kesim",Assert.Single(mapped.Project.Pieces,p=>p.Id==closed.Selection.PieceId).Metadata!.Role);
        Assert.All(import.Project.Pieces,p=>Assert.Null(p.Metadata));
    }

    [Fact]
    public void ExactLayerAndUniqueSelectionsAreRequired()
    {
        var import=Read();
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        Assert.Throws<InvalidDataException>(()=>DxfTextileLayerRolePlan.Create(import,[new("cut",DxfTextileRole.Cutting)],fingerprint));
        Assert.Throws<InvalidDataException>(()=>DxfTextileLayerRolePlan.Create(import,[new("CUT",DxfTextileRole.Cutting),new("CUT",DxfTextileRole.Auxiliary)],fingerprint));
        Assert.Throws<InvalidDataException>(()=>DxfTextileLayerRolePlan.Create(import,[new("MISSING",DxfTextileRole.Auxiliary)],fingerprint));
    }

    [Fact]
    public void ChangedProjectInvalidatesBothReviewAndSourceMapping()
    {
        var import=Read();
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        import.Project.Pieces[0].Name="edited";
        Assert.Throws<InvalidDataException>(()=>DxfTextileLayerRolePlan.Create(import,[new("CUT",DxfTextileRole.Auxiliary)],fingerprint));
        var changedFingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        Assert.Throws<InvalidDataException>(()=>DxfTextileLayerRolePlan.Create(import,[new("CUT",DxfTextileRole.Auxiliary)],changedFingerprint));
    }
}
