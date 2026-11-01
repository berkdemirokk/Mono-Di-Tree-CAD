using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class RotatedNestingRecordTests
{
    private static (ProjectDocument Source,Guid Id,Dictionary<(Guid,int),int> Angles,PerCopyNestingResult Result) Fixture()
    {
        var p=new PatternPiece { Name="Ön",Points=[new(0,0),new(6,0),new(6,4),new(0,4)],
            Notches=[new NotchMark { VertexIndex=1,Position=new(6,0) }],
            Grainline=new Grainline { Start=new(1,1),End=new(1,2) },
            Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=2 } };
        var source=new ProjectDocument { Pieces=[p] };var angles=new Dictionary<(Guid,int),int> { [(p.Id,1)]=0,[(p.Id,2)]=90 };
        var result=PerCopyOrthogonalNesting.Arrange(source,[p.Id],angles,100,1,100,0,GrainDirectionPolicy.Direction.TwoWay,90);
        return (source,p.Id,angles,result);
    }

    [Fact]
    public void CapturedEvidenceIsDetachedAndReverifies()
    {
        var f=Fixture();var record=RotatedNestingRecord.Capture(f.Source,[f.Id],f.Angles,f.Result,
            GrainDirectionPolicy.Direction.TwoWay,90,100,1,0,100);
        Assert.True(record.Verify());Assert.Equal(2,record.CopyAngles().Count);Assert.Equal(f.Id,Assert.Single(record.CopySelection()));
        f.Source.Pieces[0].Notches![0].Position=new(999,999);f.Angles[(f.Id,2)]=0;f.Result.Plan.Nesting.Layout.Pieces[0].Notches=null;
        var copy=record.CopySource();copy.Pieces[0].Points[0]=new(999,999);
        var resultCopy=record.CopyResult();resultCopy.Plan.Nesting.Layout.Pieces[0].Notches=null;
        Assert.True(record.Verify());Assert.NotNull(record.CopyResult().Plan.Nesting.Layout.Pieces[0].Notches);
        Assert.Equal(new Point2(0,0),record.CopySource().Pieces[0].Points[0]);
    }

    [Fact]
    public void RejectsForgedMarkersConstraintsAndCancellation()
    {
        var f=Fixture();f.Result.Plan.Nesting.Layout.Pieces[0].Notches=null;
        var forged=f.Result with { LayoutFingerprint=NestingSnapshot.ComputeLayoutFingerprint(f.Result.Plan.Nesting.Layout) };
        Assert.Throws<InvalidOperationException>(()=>RotatedNestingRecord.Capture(f.Source,[f.Id],f.Angles,forged,
            GrainDirectionPolicy.Direction.TwoWay,90,100,1,0,100));
        f=Fixture();
        Assert.Throws<InvalidOperationException>(()=>RotatedNestingRecord.Capture(f.Source,[f.Id],f.Angles,f.Result,
            GrainDirectionPolicy.Direction.TwoWay,90,99,1,0,100));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>RotatedNestingRecord.Capture(f.Source,[f.Id],f.Angles,f.Result,
            GrainDirectionPolicy.Direction.TwoWay,90,100,1,0,100,cancelled.Token));
    }

    [Fact]
    public void NullMarkerElementsAreRejectedBeforeCloning()
    {
        var f=Fixture();f.Source.Pieces[0].Notches![0]=null!;
        Assert.Throws<ArgumentException>(()=>RotatedNestingRecord.Capture(f.Source,[f.Id],f.Angles,f.Result,
            GrainDirectionPolicy.Direction.TwoWay,90,100,1,0,100));
        f=Fixture();f.Result.Plan.Nesting.Layout.Pieces[0].Notches![0]=null!;
        Assert.Throws<ArgumentException>(()=>RotatedNestingRecord.Capture(f.Source,[f.Id],f.Angles,f.Result,
            GrainDirectionPolicy.Direction.TwoWay,90,100,1,0,100));
    }

    [Fact]
    public void TraceModelRoundTripReverifiesAgainstLayoutAndRejectsTampering()
    {
        var f=Fixture();var original=RotatedNestingRecord.Capture(f.Source,[f.Id],f.Angles,f.Result,
            GrainDirectionPolicy.Direction.TwoWay,90,100,1,0,100);
        var trace=original.ToTrace();var layout=original.CopyResult().Plan.Nesting.Layout;
        var restored=RotatedNestingRecord.FromTrace(trace,layout);
        Assert.True(restored.Verify());Assert.True(RotatedNestingTrace.ContentEquals(trace,restored.ToTrace()));
        Assert.Equal(90,restored.CopyAngles()[(f.Id,2)]);
        var changed=trace.Clone();changed.Copies[1]=changed.Copies[1] with { Degrees=0 };
        Assert.Throws<InvalidOperationException>(()=>RotatedNestingRecord.FromTrace(changed,layout));
        changed=trace.Clone();changed.Gap=2;
        Assert.Throws<InvalidOperationException>(()=>RotatedNestingRecord.FromTrace(changed,layout));
        var badLayout=layout.Clone();badLayout.Pieces[0].Notches=null;
        changed=trace.Clone();changed.LayoutFingerprint=NestingSnapshot.ComputeLayoutFingerprint(badLayout);
        Assert.Throws<InvalidOperationException>(()=>RotatedNestingRecord.FromTrace(changed,badLayout));
        changed=trace.Clone();changed.Schema="unknown";
        Assert.Throws<InvalidDataException>(()=>RotatedNestingRecord.FromTrace(changed,layout));
        var historical=layout.Clone();historical.NestingSnapshot=new NestingSnapshot();
        Assert.Throws<InvalidDataException>(()=>RotatedNestingRecord.FromTrace(trace,historical));
        historical=layout.Clone();historical.ImportProvenance=new DxfImportProvenance();
        Assert.Throws<InvalidDataException>(()=>RotatedNestingRecord.FromTrace(trace,historical));
        historical=layout.Clone();historical.GradingRules.Add(new GradingRuleSet());
        Assert.Throws<InvalidDataException>(()=>RotatedNestingRecord.FromTrace(trace,historical));
    }
}
