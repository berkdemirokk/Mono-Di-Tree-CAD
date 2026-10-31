using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfExportAnalysisTests
{
    private static PatternPiece Piece()=>new()
    {
        Name="CUT",Closed=true,Points=[new(0,0),new(10,0),new(0,10)]
    };

    [Fact]
    public void ReportsEachTextileFieldAndUnsupportedGrainlineWithPieceIdentity()
    {
        var piece=Piece();
        piece.Metadata=new PieceMetadata { Role="Yardımcı",Size="M",Material="Cotton",Quantity=3 };
        piece.Grainline=new Grainline { Start=new(1,1),End=new(1,4),NeedsReview=true };
        var project=new ProjectDocument { Pieces=[piece] };
        var report=DxfFormat.AnalyzeExport(project);
        Assert.Equal(NestingSnapshot.ComputeLayoutFingerprint(project),report.PieceFingerprint);
        Assert.Equal(piece.Id,Assert.Single(report.Layers).PieceId);
        Assert.Equal(new[] { DxfExportOmissionKind.PieceRole,DxfExportOmissionKind.Size,DxfExportOmissionKind.Material,
            DxfExportOmissionKind.Quantity,DxfExportOmissionKind.GrainlineUnsupported },report.Omissions.Select(o=>o.Kind));
        Assert.All(report.Omissions,o=>Assert.Equal(piece.Id,o.PieceId));
        piece.Metadata.Size="L";
        Assert.Equal("CUT",Assert.Single(report.Layers).OriginalName);
        Assert.Equal(5,report.Omissions.Length);
    }

    [Fact]
    public void ReportsHistoricalProvenanceAsProjectLevelOmission()
    {
        var piece=Piece();
        var project=new ProjectDocument { Pieces=[piece],ImportProvenance=new DxfImportProvenance {
            SourceFileName="source.dxf",SourceSha256=new string('A',64),SourceByteLength=10,MillimetersPerUnit=1,PieceIds=[piece.Id] } };
        var omission=Assert.Single(DxfFormat.AnalyzeExport(project).Omissions);
        Assert.Equal(DxfExportOmissionKind.ImportProvenance,omission.Kind);
        Assert.Null(omission.PieceId);
    }

    [Fact]
    public void GeometryOnlyProjectHasNoMetadataOmissions()
    {
        var report=DxfFormat.AnalyzeExport(new ProjectDocument { Pieces=[Piece()] });
        Assert.Empty(report.Omissions);
        Assert.Single(report.Layers);
    }

    [Fact]
    public void PrecancelledAnalysisDoesNotModifyProject()
    {
        var piece=Piece();var project=new ProjectDocument { Pieces=[piece] };
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>DxfFormat.AnalyzeExport(project,cancellation.Token));
        Assert.Equal("CUT",piece.Name);Assert.Null(piece.Metadata);
    }
}
