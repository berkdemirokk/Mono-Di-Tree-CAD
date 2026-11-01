using TekstilCAD.Core;
using TekstilCAD.Nesting;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class MarkedOrthogonalNestingTests
{
    private static PatternPiece Source()
    {
        var nz=BitConverter.Int64BitsToDouble(long.MinValue);
        return new PatternPiece { Name="Ön",Points=[new(0,0),new(6,0),new(6,4),new(0,4),new(0,0)],
            Notches=[new NotchMark { VertexIndex=0,Position=new(nz,0) },new NotchMark { VertexIndex=2,Position=new(6,4) }],
            Grainline=new Grainline { Start=new(1,1),End=new(1,2) },
            Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=2 } };
    }

    [Theory]
    [InlineData(0)] [InlineData(90)] [InlineData(180)] [InlineData(270)]
    public void QuarterTurnsKeepIndependentMarkersAndGrainline(int angle)
    {
        var source=Source();var before=source.Clone();var result=OrthogonalRotation.Rotate(source,angle);
        static Point2 Turn(Point2 p,int a)=>a switch { 0=>p,90=>new(-p.Y,p.X),180=>new(-p.X,-p.Y),_=>new(p.Y,-p.X) };
        for(var i=0;i<source.Notches!.Count;i++)
        {
            var expected=Turn(source.Notches[i].Position,angle);var mark=result.Notches![i];
            Assert.Equal(source.Notches[i].VertexIndex,mark.VertexIndex);Assert.Equal(result.Points[mark.VertexIndex],mark.Position);
            Assert.Equal(BitConverter.DoubleToInt64Bits(expected.X),BitConverter.DoubleToInt64Bits(mark.Position.X));
            Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Y),BitConverter.DoubleToInt64Bits(mark.Position.Y));
            Assert.NotSame(source.Notches[i],mark);
        }
        Assert.Equal(Turn(source.Grainline!.Start,angle),result.Grainline!.Start);
        Assert.Equal(Turn(source.Grainline.End,angle),result.Grainline.End);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[source] },new ProjectDocument { Pieces=[before] }));
    }

    [Fact]
    public void MixedCopyRotationsPreserveAndIndependentlyVerifyMarkers()
    {
        var piece=Source();var source=new ProjectDocument { Pieces=[piece] };
        var angles=new Dictionary<(Guid,int),int> { [(piece.Id,1)]=0,[(piece.Id,2)]=90 };
        var result=PerCopyOrthogonalNesting.Arrange(source,[piece.Id],angles,100,1,100,0,GrainDirectionPolicy.Direction.TwoWay,90);
        bool Valid(PerCopyNestingResult r)=>PerCopyTraceVerifier.IsValidForConstraints(source,[piece.Id],r,angles,GrainDirectionPolicy.Direction.TwoWay,90,100,1,0,100);
        Assert.True(Valid(result));Assert.Equal(2,result.Plan.Nesting.Layout.Pieces.Count);
        Assert.All(result.Plan.Nesting.Layout.Pieces,p=>Assert.All(p.Notches!,m=>Assert.Equal(p.Points[m.VertexIndex],m.Position)));
        Assert.ThrowsAny<InvalidDataException>(()=>HpglFormat.Prepare(result.Plan.Nesting.Layout));
        var copy=result.Plan.Nesting.Layout.Pieces[0];copy.Notches=null;
        var forged=result with { LayoutFingerprint=NestingSnapshot.ComputeLayoutFingerprint(result.Plan.Nesting.Layout) };
        Assert.False(Valid(forged));
    }

    [Fact]
    public void SourceRotationTraceRejectsDroppedMarkerEvenWithNewLayoutFingerprint()
    {
        var piece=Source();var source=new ProjectDocument { Pieces=[piece] };
        var result=OrthogonalGrainNesting.Arrange(source,[piece.Id],100,1,100,0,[90],GrainDirectionPolicy.Direction.TwoWay,90);
        Assert.True(OrthogonalTraceVerifier.IsValid(source,[piece.Id],result));
        result.Plan.Nesting.Layout.Pieces[0].Notches=null;
        var forged=result with { LayoutFingerprint=NestingSnapshot.ComputeLayoutFingerprint(result.Plan.Nesting.Layout) };
        Assert.False(OrthogonalTraceVerifier.IsValid(source,[piece.Id],forged));
    }

    [Fact]
    public void InvalidMarkerAndUnsupportedAngleAreRejected()
    {
        var source=Source();source.Notches![0].Position=new(5,5);
        Assert.ThrowsAny<ArgumentException>(()=>OrthogonalRotation.Rotate(source,90));
        source=Source();Assert.Throws<ArgumentOutOfRangeException>(()=>OrthogonalRotation.Rotate(source,45));
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>OrthogonalRotation.Rotate(source,90,cancellation.Token));
    }
}
