using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class ALineSideBalanceTests
{
    [Fact] public void CandidateBalancesBothSegmentsAndPreservesExplicitTotalsAndSource()
    {
        var input=new ALinePanelInput(360,380,500,520,40,40,40,40,200,700,1600);
        var result=ALineSideBalance.Propose(input);Assert.True(result.Available);var candidate=result.Candidate!;
        Assert.Equal(50,candidate.FrontWaistEase);Assert.Equal(30,candidate.BackWaistEase);
        Assert.Equal(50,candidate.FrontHipEase);Assert.Equal(30,candidate.BackHipEase);
        Assert.Equal(input.FrontWaistEase+input.BackWaistEase,candidate.FrontWaistEase+candidate.BackWaistEase);
        Assert.Equal(input.FrontHipEase+input.BackHipEase,candidate.FrontHipEase+candidate.BackHipEase);
        Assert.Equal(input.FrontWaistArc,candidate.FrontWaistArc);Assert.Equal(input.BackHipArc,candidate.BackHipArc);
        Assert.Equal(40,input.FrontWaistEase);Assert.Equal(input,result.Source);
        var preview=ALinePanelPreview.Create(candidate);
        Assert.Equal(0,preview.WaistToHipMismatchMm,8);Assert.Equal(0,preview.HipToHemMismatchMm,8);
        Assert.False(preview.CanApplyOrExport);
    }
    [Fact] public void InsufficientEaseDoesNotInventNegativeEaseOrChangeSource()
    {
        var input=new ALinePanelInput(300,500,500,600,0,0,0,0,200,700,1600);
        var result=ALineSideBalance.Propose(input);
        Assert.False(result.Available);Assert.Null(result.Candidate);Assert.Equal(input,result.Source);
        Assert.Contains("Negatif",result.Explanation);
    }
    [Fact] public void InvalidGeometryIsRejectedAndNoPreviewBecomesExportable()
    {
        var input=new ALinePanelInput(360,360,500,500,20,20,20,20,700,700,1600);
        var result=ALineSideBalance.Propose(input);
        Assert.False(result.Available);Assert.Contains("geçersiz",result.Explanation);
    }
    [Fact] public void AlreadyBalancedValuesRemainUnchanged()
    {
        var input=new ALinePanelInput(360,360,500,500,20,20,20,20,200,700,1600);
        var result=ALineSideBalance.Propose(input);
        Assert.True(result.Available);Assert.Equal(input,result.Candidate);
    }
}
