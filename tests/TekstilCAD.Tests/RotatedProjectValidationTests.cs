using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class RotatedProjectValidationTests
{
    private static ProjectDocument Fixture()
    {
        var piece=new PatternPiece { Points=[new(0,0),new(6,0),new(6,4),new(0,4)],
            Notches=[new NotchMark { VertexIndex=1,Position=new(6,0) }],
            Grainline=new Grainline { Start=new(1,1),End=new(1,2) },
            Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=2 } };
        var source=new ProjectDocument { Pieces=[piece] };var angles=new Dictionary<(Guid,int),int> { [(piece.Id,1)]=0,[(piece.Id,2)]=90 };
        var result=PerCopyOrthogonalNesting.Arrange(source,[piece.Id],angles,100,1,100,0,GrainDirectionPolicy.Direction.TwoWay,90);
        var record=RotatedNestingRecord.Capture(source,[piece.Id],angles,result,GrainDirectionPolicy.Direction.TwoWay,90,100,1,0,100);
        var trace=record.ToTrace();var layout=result.Plan.Nesting.Layout.Clone();
        layout.NestingSnapshot=new NestingSnapshot { FabricWidth=100,Gap=1,EdgeMargin=0,UsedLength=result.Plan.Nesting.UsedLength,
            LayoutFingerprint=trace.LayoutFingerprint,Placements=result.Plan.Placements.Select(p=>new NestingPlacementSnapshot(p.SourcePieceId,p.CopyNumber,p.PlacedPieceId)).ToList(),RotationTrace=trace };
        return layout;
    }

    [Fact]
    public void HistoricalEvidenceRemainsVerifiableAfterVisibleLayoutEdit()
    {
        var document=Fixture();RotatedProjectValidation.Verify(document);
        Assert.True(document.NestingSnapshot!.IsCurrentFor(document));
        document.Pieces[0].Name="Düzenlenmiş";
        Assert.False(document.NestingSnapshot.IsCurrentFor(document));
        RotatedProjectValidation.Verify(document);
    }

    [Fact]
    public void MismatchedConstraintsOrForgedHistoricalMarkerAreRejected()
    {
        var document=Fixture();document.NestingSnapshot!.Gap=2;
        Assert.Throws<InvalidDataException>(()=>RotatedProjectValidation.Verify(document));
        document=Fixture();var trace=document.NestingSnapshot!.RotationTrace!;
        trace.LayoutPieces[0].Notches=null;
        trace.LayoutFingerprint=NestingSnapshot.ComputeLayoutFingerprint(new ProjectDocument { Pieces=trace.LayoutPieces });
        document.NestingSnapshot.LayoutFingerprint=trace.LayoutFingerprint;
        Assert.Throws<InvalidDataException>(()=>RotatedProjectValidation.Verify(document));
    }
}
