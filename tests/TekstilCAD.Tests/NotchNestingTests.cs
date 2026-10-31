using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public class NotchNestingTests
{
    private static PatternPiece Piece() => new()
    {
        Points=[new(0,0),new(6,0),new(6,4),new(0,4)],
        Notches=[new NotchMark { VertexIndex=1,Position=new(6,0) },new NotchMark { VertexIndex=3,Position=new(0,4) }],
        Grainline=new Grainline { Start=new(1,1),End=new(1,2) },
        Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=3 }
    };

    [Theory]
    [InlineData(0)] [InlineData(90)] [InlineData(180)] [InlineData(270)]
    public void RotationPreservesMarkerIndexAndExactRotatedVertex(int angle)
    {
        var source=Piece();var before=source.Clone();var rotated=OrthogonalRotation.Rotate(source,angle);
        foreach(var notch in rotated.Notches!) Assert.Equal(rotated.Points[notch.VertexIndex],notch.Position);
        Assert.Equal(new[] { 1,3 },rotated.Notches!.Select(n=>n.VertexIndex));
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[source] },new ProjectDocument { Pieces=[before] }));
        Assert.NotSame(source.Notches![0],rotated.Notches[0]);
    }

    [Fact]
    public void MixedCopiesKeepMarksAndTraceRejectsReattachedMarkerEvenWithNewFingerprint()
    {
        var p=Piece();var source=new ProjectDocument { Pieces=[p] };var before=source.Clone();
        var angles=new Dictionary<(Guid,int),int> { [(p.Id,1)]=0,[(p.Id,2)]=90,[(p.Id,3)]=180 };
        var result=PerCopyOrthogonalNesting.Arrange(source,[p.Id],angles,20,1,100,1,GrainDirectionPolicy.Direction.TwoWay,90);
        Assert.True(PerCopyTraceVerifier.IsValidForConstraints(source,[p.Id],result,angles,GrainDirectionPolicy.Direction.TwoWay,90,20,1,1,100));
        ShelfLayoutVerifier.Verify(result.Plan.Nesting);
        Assert.Equal(3,result.Plan.Placements.Select(x=>x.PlacedPieceId).Distinct().Count());
        foreach(var copy in result.Plan.Nesting.Layout.Pieces)
        {
            Assert.Equal(new[] { 1,3 },copy.Notches!.Select(n=>n.VertexIndex));
            Assert.All(copy.Notches,n=>Assert.Equal(copy.Points[n.VertexIndex],n.Position));
            Assert.NotSame(p.Notches![0],copy.Notches[0]);
        }
        Assert.True(DocumentSession.ContentEquals(before,source));
        var tampered=result.Plan.Nesting.Layout.Pieces[0];tampered.Notches![0].VertexIndex=0;tampered.Notches[0].Position=tampered.Points[0];
        var rebound=result with { LayoutFingerprint=NestingSnapshot.ComputeLayoutFingerprint(result.Plan.Nesting.Layout) };
        Assert.False(PerCopyTraceVerifier.IsValidForConstraints(source,[p.Id],rebound,angles,GrainDirectionPolicy.Direction.TwoWay,90,20,1,1,100));
    }

    [Fact]
    public void FixedRotationTraceRejectsDifferentValidMarkerWithRecomputedFingerprint()
    {
        var p=Piece();var source=new ProjectDocument { Pieces=[p] };
        var result=OrthogonalGrainNesting.Arrange(source,[p.Id],20,1,100,1,[90],GrainDirectionPolicy.Direction.TwoWay,90);
        Assert.True(OrthogonalTraceVerifier.IsValid(source,[p.Id],result));
        var placed=result.Plan.Nesting.Layout.Pieces[0];placed.Notches![0].VertexIndex=0;placed.Notches[0].Position=placed.Points[0];
        var rebound=result with { LayoutFingerprint=NestingSnapshot.ComputeLayoutFingerprint(result.Plan.Nesting.Layout) };
        Assert.False(OrthogonalTraceVerifier.IsValid(source,[p.Id],rebound));
    }
}
