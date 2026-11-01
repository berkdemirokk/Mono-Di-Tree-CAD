using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public sealed class RotatedNestingTraceTests
{
    [Fact]
    public void MalformedMutableListsFailBeforeCloneOrComparison()
    {
        var bad=new RotatedNestingTrace { SourcePieces=[null!] };
        Assert.Throws<ArgumentException>(()=>bad.Clone());
        Assert.Throws<ArgumentException>(()=>RotatedNestingTrace.ContentEquals(bad,new RotatedNestingTrace()));
        bad=new RotatedNestingTrace { SourcePieces=[new PatternPiece { Points=[new(0,0)],Notches=[null!] }] };
        Assert.Throws<ArgumentException>(()=>bad.Clone());
        bad=new RotatedNestingTrace { Copies=null! };Assert.Throws<ArgumentException>(()=>bad.Clone());
    }
    [Fact]
    public void CloneOwnsAllMutableEvidenceAndComparesPersistedBits()
    {
        var zero=BitConverter.Int64BitsToDouble(long.MinValue);
        var piece=new PatternPiece { Points=[new(0,0),new(10,0),new(0,10)],
            Notches=[new NotchMark { VertexIndex=0,Position=new(zero,0) }],
            Grainline=new Grainline { Start=new(1,1),End=new(2,1) },
            Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=2 } };
        var trace=new RotatedNestingTrace { Schema=RotatedNestingTrace.CurrentSchema,Algorithm=RotatedNestingTrace.CurrentAlgorithm,
            TransformConvention=RotatedNestingTrace.CurrentTransform,SourcePieces=[piece],Selections=[piece.Id],
            Copies=[new RotatedNestingCopy(piece.Id,1,Guid.NewGuid(),90)],Gap=zero,MaxLength=null,GrainDirection="TwoWay" };
        var clone=trace.Clone();Assert.True(RotatedNestingTrace.ContentEquals(trace,clone));
        Assert.NotSame(trace.SourcePieces,clone.SourcePieces);Assert.NotSame(piece,clone.SourcePieces[0]);
        Assert.NotSame(piece.Notches,clone.SourcePieces[0].Notches);
        clone.SourcePieces[0].Notches![0].Position=new(0,0);
        Assert.False(RotatedNestingTrace.ContentEquals(trace,clone));
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(piece.Notches![0].Position.X));
        clone=trace.Clone();clone.Gap=0;Assert.False(RotatedNestingTrace.ContentEquals(trace,clone));
        clone=trace.Clone();clone.MaxLength=0;Assert.False(RotatedNestingTrace.ContentEquals(trace,clone));
        clone=trace.Clone();clone.Copies[0]=clone.Copies[0] with { Degrees=0 };Assert.False(RotatedNestingTrace.ContentEquals(trace,clone));
    }
}
