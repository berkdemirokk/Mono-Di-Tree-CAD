using DressSeamBridge;
using TekstilCAD.Patterns;
using Xunit;

public class DressSeamReportTests
{
    static Request Sample()
    {
        var input=new ALinePanelInput(350,370,480,520,10,20,20,20,200,700,1600);
        return new(input,input,true,true,"snapshot-1",new(ALinePanelPreview.ModelId,"v1","lower-hash","lower-draft"),null,
            new[]{new UpperCandidate(new("Breanna","4.10.2","upper-hash","upper-draft"),"front","op-1",true)});
    }
    [Fact] public void UsesActualLowerGeometryAndPreservesSeparateProvenance()
    {
        var request=Sample(); var report=DressSeamReport.Create(request); var original=ALinePanelPreview.Create(request.Input);
        Assert.Equal(original.Front[0],report.LowerPanels[0].Waist.StartMm);
        Assert.Equal(original.Front[1],report.LowerPanels[0].Waist.EndMm);
        Assert.Equal(original.Back[1],report.LowerPanels[1].Waist.EndMm);
        Assert.Equal(report.LowerPanels[0].Side[0].EndMm,report.LowerPanels[0].Side[1].StartMm);
        Assert.Equal(original.FrontSideLengthMm,report.LowerPanels[0].Side.Sum(x=>x.LengthMm));
        Assert.NotEqual(report.LowerSource,report.UpperCandidates[0].Source);
        Assert.Equal(720,report.DerivedWaistMm); Assert.Null(report.IndependentWaist);
        Assert.Contains("unresolved",report.RelationStatus); Assert.False(report.GeometryExportAllowed); Assert.False(report.ProductionAllowed); Assert.False(report.DeviceAllowed);
    }
    [Fact] public void IndependentTotalMustAgreeAndHaveSource()
    {
        var r=Sample(); Assert.Equal(720,DressSeamReport.Create(r with { IndependentWaist=new(720,"declared measurement") }).IndependentWaist!.Millimeters);
        Assert.Throws<ArgumentException>(()=>DressSeamReport.Create(r with { IndependentWaist=new(721,"declared") }));
        Assert.Throws<ArgumentException>(()=>DressSeamReport.Create(r with { IndependentWaist=new(720,"") }));
    }
    [Fact] public void ChangedInputOrUnconfirmedSymmetryIsRejected()
    {
        var r=Sample(); Assert.Throws<ArgumentException>(()=>DressSeamReport.Create(r with { Input=r.Input with { FrontWaistArc=351 } }));
        Assert.Throws<ArgumentException>(()=>DressSeamReport.Create(r with { Confirmed=false }));
        Assert.Throws<ArgumentException>(()=>DressSeamReport.Create(r with { SymmetryAccepted=false }));
    }
    [Fact] public void UnknownOrUntrustedUpperIsNeverPaired()
    {
        var r=Sample(); Assert.Empty(DressSeamReport.Create(r with { UpperCandidates=Array.Empty<UpperCandidate>() }).UpperCandidates);
        var c=r.UpperCandidates[0];
        Assert.Throws<ArgumentException>(()=>DressSeamReport.Create(r with { UpperCandidates=new[]{c with { DeclaredSynthetic=false }} }));
        Assert.Throws<ArgumentException>(()=>DressSeamReport.Create(r with { UpperCandidates=new[]{c with { Source=r.LowerSource }} }));
        Assert.Throws<ArgumentException>(()=>DressSeamReport.Create(r with { UpperCandidates=new[]{c,c} }));
        Assert.Throws<ArgumentException>(()=>DressSeamReport.Create(r with { UpperCandidates=new[]{c with { Edge="" }} }));
    }
    [Fact] public void CopiesCandidateCollection()
    {
        var r=Sample(); var list=r.UpperCandidates.ToList(); var report=DressSeamReport.Create(r with { UpperCandidates=list }); list.Clear(); Assert.Single(report.UpperCandidates);
    }
}
