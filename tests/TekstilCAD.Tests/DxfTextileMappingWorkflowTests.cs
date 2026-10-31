using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfTextileMappingWorkflowTests
{
    private static DxfImportResult Read(string label="M",string extraText="")
    {
        var source="0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n"+
            "0\nLAYER\n2\nLABEL\n70\n0\n62\n7\n6\nCONTINUOUS\n"+
            "0\nLAYER\n2\nCUT\n70\n0\n62\n7\n6\nCONTINUOUS\n"+
            "0\nLAYER\n2\nGRAIN\n70\n0\n62\n7\n6\nCONTINUOUS\n0\nENDTAB\n0\nENDSEC\n"+
            "0\nSECTION\n2\nENTITIES\n0\nTEXT\n8\nLABEL\n1\n"+label+"\n"+extraText+
            "0\nLWPOLYLINE\n8\nCUT\n90\n3\n70\n1\n10\n0\n20\n0\n10\n20\n20\n0\n10\n0\n20\n20\n"+
            "0\nLINE\n8\nGRAIN\n10\n2\n20\n2\n11\n2\n21\n8\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        return DxfFormat.Import(stream,"workflow");
    }

    [Fact]
    public void TextAndGrainlineCanBeAppliedTogetherToSameContour()
    {
        var import=Read();var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var marker=Assert.Single(import.Project.Pieces,p=>!p.Closed);
        var text=Assert.Single(import.Annotations!.Records);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var result=DxfTextileMappingWorkflow.Apply(import,[new(contour.Id,text.SourceOrdinal,DxfTextileTextField.Size)],
            [new(contour.Id,marker.Id)],fingerprint);
        var piece=Assert.Single(result.Project.Pieces);
        Assert.Equal("M",piece.Metadata!.Size);
        Assert.Equal(marker.Points[0],piece.Grainline!.Start);
        Assert.True(piece.Grainline.NeedsReview);
        Assert.Single(result.TextAssignments);Assert.Single(result.GrainlineAssignments);
        Assert.NotNull(result.TextEvidence);Assert.NotNull(result.GrainlineSourceContext);
        Assert.Equal(2,import.Project.Pieces.Count);Assert.Null(contour.Metadata);Assert.Null(contour.Grainline);
    }

    [Fact]
    public void InvalidTextDoesNotCommitOtherwiseValidGrainline()
    {
        var import=Read(" M ");var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var marker=Assert.Single(import.Project.Pieces,p=>!p.Closed);
        var text=Assert.Single(import.Annotations!.Records);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        Assert.Throws<InvalidDataException>(()=>DxfTextileMappingWorkflow.Apply(import,
            [new(contour.Id,text.SourceOrdinal,DxfTextileTextField.Size)],[new(contour.Id,marker.Id)],fingerprint));
        Assert.Equal(2,import.Project.Pieces.Count);Assert.Null(contour.Grainline);Assert.Null(contour.Metadata);
    }

    [Fact]
    public void GrainlineOnlyDoesNotRequireTextSelection()
    {
        var import=Read();var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var marker=Assert.Single(import.Project.Pieces,p=>!p.Closed);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var result=DxfTextileMappingWorkflow.Apply(import,[],[new(contour.Id,marker.Id)],fingerprint);
        Assert.Single(result.Project.Pieces);Assert.Empty(result.TextAssignments);Assert.Null(result.TextEvidence);
    }

    [Fact]
    public void SizeMaterialQuantityAndGrainlineCanBeAssignedTogether()
    {
        var import=Read(extraText:"0\nTEXT\n8\nLABEL\n1\nCotton\n0\nTEXT\n8\nLABEL\n1\n2\n");
        var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var marker=Assert.Single(import.Project.Pieces,p=>!p.Closed);
        var text=import.Annotations!.Records;
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var result=DxfTextileMappingWorkflow.Apply(import,
            [new(contour.Id,text[0].SourceOrdinal,DxfTextileTextField.Size),
             new(contour.Id,text[1].SourceOrdinal,DxfTextileTextField.Material),
             new(contour.Id,text[2].SourceOrdinal,DxfTextileTextField.Quantity)],
            [new(contour.Id,marker.Id)],fingerprint);
        var piece=Assert.Single(result.Project.Pieces);
        Assert.Equal("M",piece.Metadata!.Size);Assert.Equal("Cotton",piece.Metadata.Material);
        Assert.Equal(2,piece.Metadata.Quantity);Assert.True(piece.Grainline!.NeedsReview);
        Assert.Equal(3,result.TextAssignments.Length);
        Assert.Null(contour.Metadata);Assert.Null(contour.Grainline);
    }

    [Fact]
    public void ExplicitNameAndGrainlineAreCombinedWithoutInventingMetadata()
    {
        var import=Read("Front");var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var marker=Assert.Single(import.Project.Pieces,p=>!p.Closed);
        var text=Assert.Single(import.Annotations!.Records);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var result=DxfTextileMappingWorkflow.Apply(import,[new(contour.Id,text.SourceOrdinal,DxfTextileTextField.PieceName)],
            [new(contour.Id,marker.Id)],fingerprint);
        var piece=Assert.Single(result.Project.Pieces);
        Assert.Equal("Front",piece.Name);Assert.Null(piece.Metadata);Assert.True(piece.Grainline!.NeedsReview);
        Assert.Equal("CUT",Assert.Single(result.TextAssignments).PreviousValue);
        Assert.Equal("CUT",contour.Name);
    }

    [Fact]
    public void NameAndAllTextileFieldsSurviveCombinedGrainlineMapping()
    {
        var import=Read("Front",extraText:"0\nTEXT\n8\nLABEL\n1\nM\n0\nTEXT\n8\nLABEL\n1\nCotton\n0\nTEXT\n8\nLABEL\n1\n2\n");
        var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var marker=Assert.Single(import.Project.Pieces,p=>!p.Closed);
        var text=import.Annotations!.Records;
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var result=DxfTextileMappingWorkflow.Apply(import,
            [new(contour.Id,text[0].SourceOrdinal,DxfTextileTextField.PieceName),
             new(contour.Id,text[1].SourceOrdinal,DxfTextileTextField.Size),
             new(contour.Id,text[2].SourceOrdinal,DxfTextileTextField.Material),
             new(contour.Id,text[3].SourceOrdinal,DxfTextileTextField.Quantity)],
            [new(contour.Id,marker.Id)],[new(contour.Id,DxfTextileRole.Cutting)],fingerprint);
        var piece=Assert.Single(result.Project.Pieces);
        Assert.Equal("Front",piece.Name);Assert.Equal("M",piece.Metadata!.Size);
        Assert.Equal("Cotton",piece.Metadata.Material);Assert.Equal(2,piece.Metadata.Quantity);Assert.Equal("Kesim",piece.Metadata.Role);
        Assert.Equal(marker.Points[0],piece.Grainline!.Start);Assert.True(piece.Grainline.NeedsReview);
        Assert.Equal("CUT",Assert.Single(result.TextAssignments,a=>a.Field==DxfTextileTextField.PieceName).PreviousValue);
        Assert.Equal(DxfTextileRole.Cutting,Assert.Single(result.RoleAssignments).Role);
        var evidence=Assert.IsType<DxfTextileImportEvidence>(result.SourceEvidence);
        Assert.Equal(import.SourceSha256,evidence.SourceSha256);
        Assert.Equal("CUT",Assert.Single(evidence.PieceSources,s=>s.PieceId==contour.Id).Layer);
        Assert.Equal("GRAIN",Assert.Single(evidence.PieceSources,s=>s.PieceId==marker.Id).Layer);
        Assert.Equal(import.PieceSourcesFingerprint,evidence.PieceSourcesFingerprint);
        Assert.Equal("CUT",contour.Name);Assert.Null(contour.Metadata);Assert.Null(contour.Grainline);
    }

    [Fact]
    public void EditedImportDoesNotExposeHistoricalPieceSourcesAsCurrent()
    {
        var import=Read();var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var marker=Assert.Single(import.Project.Pieces,p=>!p.Closed);
        contour.Name="Edited";
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var result=DxfTextileMappingWorkflow.Apply(import,[],[new(contour.Id,marker.Id)],fingerprint);
        Assert.Null(result.SourceEvidence);
        Assert.Null(Assert.Single(result.GrainlineAssignments).Source);
    }

    [Fact]
    public void DuplicateNameSelectionDoesNotApplyAnyPartOfCombinedOperation()
    {
        var import=Read("Front");var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var marker=Assert.Single(import.Project.Pieces,p=>!p.Closed);
        var text=Assert.Single(import.Annotations!.Records);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var name=new DxfTextileTextSelection(contour.Id,text.SourceOrdinal,DxfTextileTextField.PieceName);
        Assert.Throws<InvalidDataException>(()=>DxfTextileMappingWorkflow.Apply(import,[name,name],[new(contour.Id,marker.Id)],fingerprint));
        Assert.Equal("CUT",contour.Name);Assert.Null(contour.Metadata);Assert.Null(contour.Grainline);
        Assert.Equal(2,import.Project.Pieces.Count);
    }

    [Fact]
    public void RoleForConsumedMarkerCannotBeSilentlyDiscarded()
    {
        var import=Read("Front");var contour=Assert.Single(import.Project.Pieces,p=>p.Closed);
        var marker=Assert.Single(import.Project.Pieces,p=>!p.Closed);
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        Assert.Throws<InvalidDataException>(()=>DxfTextileMappingWorkflow.Apply(import,[],[new(contour.Id,marker.Id)],
            [new(marker.Id,DxfTextileRole.Auxiliary)],fingerprint));
        Assert.Equal(2,import.Project.Pieces.Count);Assert.Null(marker.Metadata);Assert.Null(contour.Grainline);
    }
}
