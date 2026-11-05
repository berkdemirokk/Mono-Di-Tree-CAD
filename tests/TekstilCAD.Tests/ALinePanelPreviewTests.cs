using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class ALinePanelPreviewTests
{
    private static ALinePanelInput Input => new(360,360,500,500,20,20,20,20,200,700,1600);
    [Fact] public void ExplicitSectionsDetermineWidthsAndLengthsWithoutDefaults()
    {
        var a=ALinePanelPreview.Create(Input);var b=ALinePanelPreview.Create(Input);
        Assert.Equal(a.Front,b.Front);Assert.Equal(a.Front,a.Back);
        Assert.Equal(190,a.Front[1].X);Assert.Equal(260,a.Front[2].X);Assert.Equal(400,a.Front[3].X);
        Assert.Equal(200,a.Front[2].Y);Assert.Equal(700,a.Front[3].Y);
        Assert.Equal(Math.Sqrt(70*70+200*200)+Math.Sqrt(140*140+500*500),a.FrontSideLengthMm,8);
        Assert.Equal(0,a.SideMismatchMm);Assert.False(a.CanApplyOrExport);Assert.NotEmpty(a.Unresolved);
    }
    [Fact] public void UnequalBodyArcsExposeSideMismatchInsteadOfSilentlyCorrecting()
    {
        var result=ALinePanelPreview.Create(Input with { BackWaistArc=380 });
        Assert.True(result.SideMismatchMm>0);Assert.Equal(200,result.Back[1].X);
        Assert.False(result.CanApplyOrExport);
    }
    [Fact] public void InvalidOrNonALineDimensionsDoNotProducePreview()
    {
        foreach(var bad in new[]{Input with { HipDepth=700 },Input with { FrontHipArc=double.NaN },
            Input with { FrontWaistEase=-1 },Input with { FinishedHemCircumference=1040 },Input with { FrontWaistArc=600 }})
            Assert.Throws<ArgumentException>(()=>ALinePanelPreview.Create(bad));
    }
    [Fact] public void UniformScaleScalesEveryCoordinateAndSideLength()
    {
        var a=ALinePanelPreview.Create(Input);
        var b=ALinePanelPreview.Create(new(720,720,1000,1000,40,40,40,40,400,1400,3200));
        for(int i=0;i<a.Front.Count;i++){Assert.Equal(a.Front[i].X*2,b.Front[i].X);Assert.Equal(a.Front[i].Y*2,b.Front[i].Y);}
        Assert.Equal(a.FrontSideLengthMm*2,b.FrontSideLengthMm,8);
    }
}
