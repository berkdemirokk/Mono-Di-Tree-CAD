using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class ALinePanelPreviewTests
{
    [Fact] public void InvalidGeometryReportsTheRelevantInputsAndKeepsTheSameRules()
    {
        var front=Assert.Throws<ArgumentException>(()=>ALinePanelPreview.Create(Input with { FrontWaistEase=200 }));
        Assert.Contains("Ön beden",front.Message);Assert.Contains("bolluk",front.Message);
        var back=Assert.Throws<ArgumentException>(()=>ALinePanelPreview.Create(Input with { BackWaistEase=200 }));
        Assert.Contains("Arka beden",back.Message);
        var depth=Assert.Throws<ArgumentException>(()=>ALinePanelPreview.Create(Input with { HipDepth=Input.WaistToHem }));
        Assert.Contains("düşüklük",depth.Message);
        var hem=Assert.Throws<ArgumentException>(()=>ALinePanelPreview.Create(Input with { FinishedHemCircumference=2*(Input.FrontHipArc+Input.FrontHipEase) }));
        Assert.Contains("etek",hem.Message);Assert.Contains("Ön beden",hem.Message);
    }
    private static ALinePanelInput Input => new(360,360,500,500,20,20,20,20,200,700,1600);
    private static string[] CmFields => ["36","36","50","50","2","2","2","2","20","70","160"];
    [Theory]
    [InlineData("1,000")]
    [InlineData("1.000")]
    [InlineData("36.125")]
    public void AmbiguousThreeDigitSeparatorIsRejectedInManualFormToo(string value)
    {
        var fields=CmFields;fields[4]=value;
        Assert.Contains("üç basamaklı ayırıcı belirsiz",Assert.Throws<ArgumentException>(()=>ALineMeasurementReview.Normalize(fields,ALineLengthUnit.Millimeter)).Message);
        fields[4]="0";Assert.Equal(0,ALineMeasurementReview.Normalize(fields,ALineLengthUnit.Millimeter).FrontWaistEase);
        fields[4]="1.25";Assert.Equal(1.25,ALineMeasurementReview.Normalize(fields,ALineLengthUnit.Millimeter).FrontWaistEase);
        fields[4]="1000";Assert.Equal(1000,ALineMeasurementReview.Normalize(fields,ALineLengthUnit.Millimeter).FrontWaistEase);
    }
    [Fact] public void MissingUnitCannotSilentlyInterpretCentimetersAsMillimeters()
    {
        Assert.Throws<ArgumentException>(()=>ALineMeasurementReview.Normalize(CmFields,null));
        Assert.Equal(Input,ALineMeasurementReview.Normalize(CmFields,ALineLengthUnit.Centimeter));
        Assert.Equal(Input,ALineMeasurementReview.Normalize(["360","360","500","500","20","20","20","20","200","700","1600"],ALineLengthUnit.Millimeter));
        Assert.Equal(36,ALineMeasurementReview.Normalize(CmFields,ALineLengthUnit.Millimeter).FrontWaistArc);
    }
    [Fact] public void UnitOrMeasurementChangeRequiresFreshReviewAndConfirmation()
    {
        var gate=new ALineMeasurementReview();
        Assert.Throws<InvalidOperationException>(()=>gate.Confirm());
        gate.Stage(Input);Assert.Throws<InvalidOperationException>(()=>gate.RequireConfirmed(Input));
        gate.Confirm();gate.RequireConfirmed(Input);
        Assert.Throws<InvalidOperationException>(()=>gate.RequireConfirmed(Input with { WaistToHem=710 }));
        Assert.Throws<InvalidOperationException>(()=>gate.RequireConfirmed(ALineMeasurementReview.Normalize(CmFields,ALineLengthUnit.Millimeter)));
        gate.Invalidate();Assert.Throws<InvalidOperationException>(()=>gate.Confirm());
        Assert.Throws<InvalidOperationException>(()=>gate.RequireConfirmed(Input));
        gate.Stage(Input);gate.Confirm();gate.RequireConfirmed(Input);
        Assert.Throws<ArgumentException>(()=>gate.Stage(Input with { HipDepth=900 }));
        Assert.Throws<InvalidOperationException>(()=>gate.RequireConfirmed(Input));
    }
    [Fact] public void PreviewDisclosesLeftRightSymmetryAndNeverApprovesFit()
    {
        var preview=ALinePanelPreview.Create(Input);
        Assert.Contains(preview.Unresolved,s=>s.Contains("Sağ ve sol") && s.Contains("asimetri"));
        Assert.False(preview.CanApplyOrExport);
    }
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
