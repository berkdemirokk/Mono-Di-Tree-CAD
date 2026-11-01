using System.Text;
using TekstilCAD.FileFormats;
using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public class DxfTextileGrainlineMappingTests
{
    private static DxfImportResult Read(string markerMetadata="",double factor=1)
    {
        var source="0\nSECTION\n2\nENTITIES\n"+
            "0\nLWPOLYLINE\n8\nCUT\n90\n3\n70\n1\n10\n0\n20\n0\n10\n20\n20\n0\n10\n0\n20\n20\n"+
            "0\nLINE\n8\nGRAIN\n10\n2\n20\n2\n11\n2\n21\n8\n"+markerMetadata+"0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        return DxfFormat.Import(stream,"textile",factor);
    }

    [Fact]
    public void ExplicitAssociationConsumesMarkerOnlyInCopyAndRequiresReview()
    {
        var import=Read();
        var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var marker=Assert.Single(import.Project.Pieces,p=>!p.Closed);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var result=DxfTextileGrainlineMapping.Apply(import,[new(contour.Id,marker.Id)],fingerprint);
        var piece=Assert.Single(result.Project.Pieces);
        Assert.Equal(contour.Id,piece.Id);
        Assert.Equal(marker.Points[0],piece.Grainline!.Start);
        Assert.Equal(marker.Points[1],piece.Grainline.End);
        Assert.True(piece.Grainline.NeedsReview);
        var audit=Assert.Single(result.ConsumedMarkers);
        Assert.Equal(marker.Id,audit.MarkerId);
        Assert.Equal(marker.Name,audit.MarkerName);
        Assert.Equal(contour.Id,audit.ContourId);
        Assert.Equal("LINE",audit.Source!.EntityType);
        Assert.Single(audit.Source.SourceOrdinals);
        Assert.NotNull(result.SourceContext);
        Assert.Equal(import.SourceSha256,result.SourceContext.SourceSha256);
        Assert.Equal(2,import.Project.Pieces.Count);
        Assert.Null(contour.Grainline);
    }

    [Fact]
    public void NotchedMarkerCannotBeConsumedAsGrainline()
    {
        var import=Read();
        var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var marker=Assert.Single(import.Project.Pieces,p=>!p.Closed);
        marker.Notches=[new NotchMark { VertexIndex=0,Position=marker.Points[0] }];
        var before=import.Project.Clone();
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var error=Assert.Throws<InvalidDataException>(()=>
            DxfTextileGrainlineMapping.Apply(import,[new(contour.Id,marker.Id)],fingerprint));
        Assert.Contains("Çentik",error.Message);
        Assert.True(DocumentSession.ContentEquals(before,import.Project));
        Assert.Single(marker.Notches);
        Assert.Null(contour.Grainline);
    }

    [Fact]
    public void GrainlineCanBeAddedToNotchedContourWithoutChangingMarks()
    {
        var import=Read();
        var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var marker=Assert.Single(import.Project.Pieces,p=>!p.Closed);
        contour.Notches=[new NotchMark { VertexIndex=1,Position=contour.Points[1] }];
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var result=DxfTextileGrainlineMapping.Apply(import,[new(contour.Id,marker.Id)],fingerprint);
        var mapped=Assert.Single(result.Project.Pieces);
        var mark=Assert.Single(mapped.Notches!);
        Assert.Equal(1,mark.VertexIndex);
        Assert.Equal(contour.Points[1],mark.Position);
        Assert.NotNull(mapped.Grainline);
        Assert.Single(contour.Notches);
    }

    [Fact]
    public void DuplicateOrMissingSelectionsFailWithoutMutatingImport()
    {
        var import=Read();
        var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var marker=Assert.Single(import.Project.Pieces,p=>!p.Closed);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        Assert.Throws<InvalidDataException>(()=>DxfTextileGrainlineMapping.Apply(import,[new(contour.Id,marker.Id),new(contour.Id,marker.Id)],fingerprint));
        Assert.Throws<InvalidDataException>(()=>DxfTextileGrainlineMapping.Apply(import,[new(contour.Id,Guid.NewGuid())],fingerprint));
        Assert.Equal(2,import.Project.Pieces.Count);
        Assert.Null(contour.Grainline);
    }

    [Fact]
    public void PrecancelledOperationDoesNotChangeImport()
    {
        var import=Read();
        using var cancellation=new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>DxfTextileGrainlineMapping.Apply(import,[],"",cancellation.Token));
        Assert.Equal(2,import.Project.Pieces.Count);
    }

    [Fact]
    public void EditedMarkerInvalidatesPreviousReview()
    {
        var import=Read();
        var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var marker=Assert.Single(import.Project.Pieces,p=>!p.Closed);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        marker.Name="changed";
        Assert.Throws<InvalidDataException>(()=>DxfTextileGrainlineMapping.Apply(import,[new(contour.Id,marker.Id)],fingerprint));
        Assert.Equal(2,import.Project.Pieces.Count);
    }

    [Fact]
    public void RereviewAfterEditDoesNotClaimOldSourceStillDescribesMarker()
    {
        var import=Read();
        var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var marker=Assert.Single(import.Project.Pieces,p=>!p.Closed);
        marker.Name="edited";
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var result=DxfTextileGrainlineMapping.Apply(import,[new(contour.Id,marker.Id)],fingerprint);
        Assert.Null(Assert.Single(result.ConsumedMarkers).Source);
        Assert.Null(result.SourceContext);
    }

    [Fact]
    public void ConsumedMarkerRetainsOpaqueSourceContextForReview()
    {
        var import=Read("1001\nVENDOR\n1000\n  mark  \n");
        var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var marker=Assert.Single(import.Project.Pieces,p=>!p.Closed);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var result=DxfTextileGrainlineMapping.Apply(import,[new(contour.Id,marker.Id)],fingerprint);
        var origin=Assert.Single(result.ConsumedMarkers).Source!;
        var context=Assert.IsType<DxfGrainlineSourceContext>(result.SourceContext);
        Assert.Equal(import.SourceSha256,context.SourceSha256);
        Assert.Contains(context.OpaqueMetadata!.Groups,g=>g.RawValue=="  mark  " && origin.SourceOrdinals.Contains(g.SourceOrdinal));
        Assert.Contains(context.Warnings,w=>w.Contains("genişletilmiş meta verileri"));
        import.Warnings.Clear();
        Assert.NotEmpty(context.Warnings);
    }

    [Fact]
    public void UnknownOrUnsafeBlockScopeCannotBeReviewed()
    {
        var import=Read();
        var unknown=new DxfImportResult(import.Project,[]);
        Assert.Throws<InvalidDataException>(()=>DxfTextileGrainlineMapping.CaptureFingerprint(unknown));
        var unsafeImport=import with { ImportedBlockDefinitions=true };
        Assert.Throws<InvalidDataException>(()=>DxfTextileGrainlineMapping.CaptureFingerprint(unsafeImport));
    }

    [Fact]
    public void ReviewTokenBindsSourceBytesAndImportSettingsEvenWhenProjectIsIdentical()
    {
        var import=Read();var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var marker=Assert.Single(import.Project.Pieces,p=>!p.Closed);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var differentSource=Read("1000\nother\n") with { Project=import.Project.Clone() };
        var differentScale=Read(factor:2) with { Project=import.Project.Clone() };
        Assert.NotEqual(fingerprint,DxfTextileGrainlineMapping.CaptureFingerprint(differentSource));
        Assert.NotEqual(fingerprint,DxfTextileGrainlineMapping.CaptureFingerprint(differentScale));
        Assert.Throws<InvalidDataException>(()=>DxfTextileGrainlineMapping.Apply(differentSource,[new(contour.Id,marker.Id)],fingerprint));
        Assert.Throws<InvalidDataException>(()=>DxfTextileGrainlineMapping.Apply(differentScale,[new(contour.Id,marker.Id)],fingerprint));
    }

    [Fact]
    public void SourceIdentityAndImportSafetyFlagsCannotBeReplacedAfterCapture()
    {
        var import=Read();var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var marker=Assert.Single(import.Project.Pieces,p=>!p.Closed);
        var changedHash=import with { SourceSha256=new string('0',64) };
        var changedFlags=import with { SkippedRelevantInsertEntities=true };
        Assert.Throws<InvalidDataException>(()=>DxfTextileGrainlineMapping.Apply(changedHash,[new(contour.Id,marker.Id)],fingerprint));
        Assert.Throws<InvalidDataException>(()=>DxfTextileGrainlineMapping.Apply(changedFlags,[new(contour.Id,marker.Id)],fingerprint));
    }

    [Fact]
    public void SourceEvidenceUsesCapturedWarningsInsteadOfMutableList()
    {
        var import=Read("1000\nprivate\n");
        var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var marker=Assert.Single(import.Project.Pieces,p=>!p.Closed);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        import.Warnings.Clear();
        var result=DxfTextileGrainlineMapping.Apply(import,[new(contour.Id,marker.Id)],fingerprint);
        Assert.Contains(result.SourceContext!.Warnings,w=>w.Contains("genişletilmiş meta verileri"));
    }
}
