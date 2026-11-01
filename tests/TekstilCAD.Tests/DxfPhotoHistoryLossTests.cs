using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfPhotoHistoryLossTests
{
    private static PhotoTraceProvenance History(Guid pieceId)=>new()
    {
        AlgorithmVersion=1,PieceId=pieceId,SourceSha256=new string('A',64),SourceByteLength=123,SourceOrientation=1,
        SourceWidth=100,SourceHeight=100,PreviewWidth=100,PreviewHeight=100,
        ReferenceStart=new(0,0),ReferenceEnd=new(10,0),ReferenceLengthMm=10,
        TracePixels=[new(0,0),new(10,0)],GeneratedPoints=[new(0,0),new(10,0)]
    };
    private static ProjectDocument Project(bool history=true,bool marks=false,bool grain=false)
    {
        var first=new PatternPiece { Name="Ön",Closed=true,Points=[new(0,0),new(20,0),new(20,20),new(0,20)] };
        var second=new PatternPiece { Name="Arka",Closed=true,Points=[new(30,0),new(50,0),new(50,20),new(30,20)] };
        if(marks)first.Notches=[new NotchMark { VertexIndex=0,Position=first.Points[0] }];
        if(grain)first.Grainline=new Grainline { Start=new(5,2),End=new(5,18),NeedsReview=true };
        // Deliberately associate the historical trace with the unselected second
        // piece: the omission is document-wide, not a selection-level claim.
        return new ProjectDocument { Name="Model",Pieces=[first,second],PhotoTraceHistory=history ? [History(second.Id)] : null };
    }
    private static void HasExactlyOneGlobal(IEnumerable<DxfExportOmission> omissions)
    {
        var item=Assert.Single(omissions,x=>x.Kind==DxfExportOmissionKind.PhotoTraceHistory);
        Assert.Null(item.PieceId);
    }
    [Fact]
    public void OrdinaryAnalysisReportsGlobalLossOnlyWhenHistoryExists()
    {
        var without=Project(history:false);var noHistory=DxfFormat.AnalyzeExport(without);
        Assert.DoesNotContain(noHistory.Omissions,x=>x.Kind==DxfExportOmissionKind.PhotoTraceHistory);
        var withHistory=Project();var before=withHistory.Clone();var analysis=DxfFormat.AnalyzeExport(withHistory);
        HasExactlyOneGlobal(analysis.Omissions);Assert.True(DocumentSession.ContentEquals(before,withHistory));
    }
    [Fact]
    public void AllPieceNotchReviewReportsHistoryWithoutWritingItIntoDxf()
    {
        var source=Project(marks:true,grain:true);var before=source.Clone();
        var plan=DxfNotchReviewExport.Prepare(source,false);
        HasExactlyOneGlobal(plan.Omissions);Assert.Single(plan.Grainlines);Assert.Equal(2,plan.Contours.Length);
        Assert.DoesNotContain(source.PhotoTraceHistory![0].SourceSha256,plan.Text,StringComparison.Ordinal);
        Assert.True(plan.Matches(source));Assert.True(DocumentSession.ContentEquals(before,source));
        source.PhotoTraceHistory[0].SourceSha256=new string('B',64);Assert.False(plan.Matches(source));
    }
    [Fact]
    public void SelectedReviewDisclosesDocumentHistoryEvenWhenItsPieceWasNotSelected()
    {
        var source=Project(marks:true,grain:true);var before=source.Clone();var selected=source.Pieces[0].Id;
        var plan=DxfNotchReviewExport.PrepareSelected(source,[selected],true);
        HasExactlyOneGlobal(plan.Omissions);Assert.Single(plan.Contours);Assert.Equal(selected,plan.Contours[0].PieceId);
        Assert.Single(plan.Grainlines);Assert.DoesNotContain(plan.Contours,x=>x.PieceId==source.Pieces[1].Id);
        Assert.DoesNotContain(source.PhotoTraceHistory![0].SourceSha256,plan.Text,StringComparison.Ordinal);
        Assert.True(plan.Matches(source));Assert.True(DocumentSession.ContentEquals(before,source));
        source.PhotoTraceHistory[0].SourceSha256=new string('B',64);Assert.False(plan.Matches(source));
    }
    [Fact]
    public void SelectedReviewWithoutHistoryRetainsExistingOmissionsAndRejectsNoMarks()
    {
        var source=Project(history:false,marks:true);var plan=DxfNotchReviewExport.PrepareSelected(source,[source.Pieces[0].Id],false);
        Assert.DoesNotContain(plan.Omissions,x=>x.Kind==DxfExportOmissionKind.PhotoTraceHistory);
        var before=source.Clone();Assert.Throws<ArgumentException>(()=>DxfNotchReviewExport.PrepareSelected(source,[source.Pieces[1].Id],false));
        Assert.True(DocumentSession.ContentEquals(before,source));
    }
    [Fact]
    public void GrainlineReviewCarriesGlobalOmissionAndBindsEntireSource()
    {
        var source=Project(grain:true);var before=source.Clone();var plan=DxfGrainlineReviewExport.Prepare(source);
        HasExactlyOneGlobal(plan.Omissions);Assert.Contains(plan.Omissions,x=>x.Kind==DxfExportOmissionKind.GrainlineReviewState);
        Assert.DoesNotContain(source.PhotoTraceHistory![0].SourceSha256,plan.Text,StringComparison.Ordinal);
        Assert.True(plan.Matches(source));Assert.True(DocumentSession.ContentEquals(before,source));
        source.PhotoTraceHistory[0].SourceSha256=new string('B',64);Assert.False(plan.Matches(source));
    }
    [Fact]
    public void CancelledReviewDoesNotChangeSource()
    {
        var source=Project(marks:true);var before=source.Clone();using var token=new CancellationTokenSource();token.Cancel();
        Assert.Throws<OperationCanceledException>(()=>DxfNotchReviewExport.PrepareSelected(source,[source.Pieces[0].Id],false,token.Token));
        Assert.True(DocumentSession.ContentEquals(before,source));
    }
}
