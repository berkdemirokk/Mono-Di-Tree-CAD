using TekstilCAD.Patterns;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class ALineTransferPlanTests
{
    private static string[] Empty()=>Enumerable.Repeat("",11).ToArray();
    private static decimal?[] Partial()=>[360m,null,null,null,0m,null,null,null,null,null,null];
    [Fact] public void PartialPlanRequiresUnitAndSourceWithoutIssuesAndPreservesMissingEase()
    {
        Assert.Throws<ArgumentException>(()=>ALineTransferPlan.Create(Partial(),[],Empty(),null));
        Assert.Throws<ArgumentException>(()=>ALineTransferPlan.Create(Partial(),["koşullu"],Empty(),ALineLengthUnit.Millimeter));
        Assert.Throws<ArgumentException>(()=>ALineTransferPlan.Create(new decimal?[11],[],Empty(),ALineLengthUnit.Millimeter));
        var plan=ALineTransferPlan.Create(Partial(),[],Empty(),ALineLengthUnit.Centimeter);
        var after=plan.ApplyTo(Empty(),ALineLengthUnit.Centimeter);
        Assert.Equal("36",after[0]);Assert.Equal("0",after[4]);Assert.Equal("",after[5]);
        Assert.Equal(2,plan.Rows.Count(r=>r.ProposedText is not null));
        Assert.Throws<ArgumentException>(()=>ALineMeasurementReview.Normalize(after,ALineLengthUnit.Centimeter));
    }
    [Fact] public void ThreeFractionalDigitsUseAnExactReadableRepresentationWithoutRounding()
    {
        var source=ALineRequestMeasurements.Review("ön bel yayı bolluğu 0.0125 cm","");
        var plan=ALineTransferPlan.Create(source.Millimeters,source.Issues,Empty(),ALineLengthUnit.Millimeter);
        var after=plan.ApplyTo(Empty(),ALineLengthUnit.Millimeter);
        Assert.Equal("0.1250",after[4]);Assert.Equal(0.125m,ALineMeasurementReview.ParseLengthMm(after[4],ALineLengthUnit.Millimeter,4));
        var cm=ALineTransferPlan.Create(source.Millimeters,source.Issues,Empty(),ALineLengthUnit.Centimeter).ApplyTo(Empty(),ALineLengthUnit.Centimeter);
        Assert.Equal("0.0125",cm[4]);
    }
    [Fact] public void FilledValuesArePreservedAndAConflictingValueBlocksAllWrites()
    {
        var current=Empty();current[0]=" 36,0 ";current[1]="37";
        var plan=ALineTransferPlan.Create(Partial(),[],current,ALineLengthUnit.Centimeter);
        var after=plan.ApplyTo(current,ALineLengthUnit.Centimeter);
        Assert.Equal(current[0],after[0]);Assert.Equal(current[1],after[1]);Assert.Equal("0",after[4]);
        current[0]="35";
        Assert.Throws<ArgumentException>(()=>ALineTransferPlan.Create(Partial(),[],current,ALineLengthUnit.Centimeter));
        Assert.Equal("35",current[0]);Assert.Equal("",current[4]);
    }
    [Fact] public void PlanOwnsItsSnapshotAndRejectsFieldOrUnitChanges()
    {
        var source=Partial();var current=Empty();var plan=ALineTransferPlan.Create(source,[],current,ALineLengthUnit.Millimeter);
        source[0]=500;current[1]="30";
        Assert.Throws<InvalidOperationException>(()=>plan.ApplyTo(current,ALineLengthUnit.Millimeter));
        Assert.Throws<InvalidOperationException>(()=>plan.ApplyTo(Empty(),ALineLengthUnit.Centimeter));
        Assert.Equal("360",plan.ApplyTo(Empty(),ALineLengthUnit.Millimeter)[0]);
    }
    [Fact] public void ExplicitPartialContradictionsAreRejectedWithoutAssumingMissingEase()
    {
        var source=Partial();source[8]=700;source[9]=200;
        Assert.Throws<ArgumentException>(()=>ALineTransferPlan.Create(source,[],Empty(),ALineLengthUnit.Millimeter));
        Assert.NotEmpty(ALineRequestMeasurements.Review("ön bel yayı 36 cm","bel çevresi 35 cm").Issues);
        var partial=ALineTransferPlan.Create(Partial(),[],Empty(),ALineLengthUnit.Millimeter);
        Assert.Null(partial.Rows[6].SourceMillimeters);Assert.Null(partial.Rows[6].ProposedText);
    }
    [Fact] public void AllElevenFieldsStillTransferAndPassTheSeparateGeometryGate()
    {
        decimal?[] source=[360,360,500,500,20,20,20,20,200,700,1600];
        var values=ALineTransferPlan.Create(source,[],Empty(),ALineLengthUnit.Centimeter).ApplyTo(Empty(),ALineLengthUnit.Centimeter);
        var input=ALineMeasurementReview.Normalize(values,ALineLengthUnit.Centimeter);
        var gate=new ALineMeasurementReview();gate.Stage(input);
        Assert.Throws<InvalidOperationException>(()=>gate.RequireConfirmed(input));
        gate.Confirm();gate.RequireConfirmed(input);Assert.False(ALinePanelPreview.Create(input).CanApplyOrExport);
    }
}
