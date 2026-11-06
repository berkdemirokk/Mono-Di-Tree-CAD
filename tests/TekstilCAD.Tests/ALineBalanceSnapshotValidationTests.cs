using TekstilCAD.Desktop;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class ALineBalanceSnapshotValidationTests
{
    [Fact] public void SnapshotRecomputesExactlyAndRejectsSourceFormOrCandidateChanges()
    {
        string[] fields=["360","380","500","520","40","40","40","40","200","700","1600"];
        var source=new string('A',64);var input=ALineMeasurementReview.Normalize(fields,ALineLengthUnit.Millimeter);
        var snapshot=ALineBalanceSnapshotValidation.Create(ALineSideBalance.Propose(input));
        var draft=new ALineWorkDraft("mm",fields,source,snapshot);
        var loaded=ALineWorkDraftStorage.Deserialize(ALineWorkDraftStorage.Serialize(draft));
        Assert.True(ALineBalanceSnapshotValidation.Validate(loaded,source).Available);
        Assert.Throws<InvalidDataException>(()=>ALineBalanceSnapshotValidation.Validate(loaded,new string('B',64)));
        var changed=(string[])fields.Clone();changed[4]="41";
        Assert.Throws<InvalidDataException>(()=>ALineBalanceSnapshotValidation.Validate(loaded with { Fields=changed },source));
        Assert.Throws<InvalidDataException>(()=>ALineBalanceSnapshotValidation.Validate(loaded with { Balance=snapshot with { Candidate=[0,0,0,0] } },source));
        Assert.Throws<InvalidDataException>(()=>ALineBalanceSnapshotValidation.Validate(loaded with { Unit="cm" },source));
        Assert.False(ALinePanelPreview.Create(input).CanApplyOrExport);
    }
}
