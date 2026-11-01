using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class ExtraClosureCleanupTests
{
    private static PatternPiece Source()=>new() { Closed=true,Name="Kapanış",Points=[new(0,0),new(10,0),new(10,10),new(0,10),new(0,0),new(0,0)] };
    [Fact]
    public void RemovesOnlyExtraTerminalCopiesInDetachedValidatedPiece()
    {
        var source=Source();var before=source.Clone();var edited=PatternTools.RemoveExtraClosingVertices(source);
        Assert.Equal(5,edited.Points.Count);Assert.Equal(source.Points.Take(5),edited.Points);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
        Assert.Throws<ArgumentException>(()=>PatternTools.RemoveExtraClosingVertices(edited));
    }
    [Fact]
    public void RejectsMarkedOrBitDistinctClosureAndOtherInvalidGeometry()
    {
        var source=Source();source.Notches=[new NotchMark { VertexIndex=1,Position=source.Points[1] }];
        Assert.Throws<ArgumentException>(()=>PatternTools.RemoveExtraClosingVertices(source));
        source.Notches=null;source.Points[^1]=new Point2(BitConverter.Int64BitsToDouble(long.MinValue),0);
        Assert.Throws<ArgumentException>(()=>PatternTools.RemoveExtraClosingVertices(source));
        source=Source();source.Points[2]=source.Points[1];
        var stillInvalid=PatternTools.RemoveExtraClosingVertices(source);
        Assert.Equal(stillInvalid.Points[1],stillInvalid.Points[2]);
        Assert.Equal(5,stillInvalid.Points.Count);
        source=Source();source.Closed=false;
        Assert.Throws<ArgumentException>(()=>PatternTools.RemoveExtraClosingVertices(source));
    }
    [Fact]
    public void ConsecutiveCleanupPreservesOtherPointsAndRejectsBitDistinctCopies()
    {
        var source=Source();source.Points.Insert(2,source.Points[1]);var before=source.Clone();
        var edited=PatternTools.RemoveConsecutiveDuplicateVertices(source);
        Assert.Equal(5,edited.Points.Count);Assert.Equal(new Point2(10,0),edited.Points[1]);Assert.Equal(new Point2(10,10),edited.Points[2]);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
        source.Notches=[new NotchMark { VertexIndex=1,Position=source.Points[1] }];
        var marked=PatternTools.RemoveConsecutiveDuplicateVertices(source);
        Assert.Equal(1,Assert.Single(marked.Notches!).VertexIndex);
        source=Source();source.Points[^1]=new Point2(BitConverter.Int64BitsToDouble(long.MinValue),0);
        Assert.Throws<ArgumentException>(()=>PatternTools.RemoveConsecutiveDuplicateVertices(source));
    }
    [Fact]
    public void MarkedCleanupRemapsInteriorAndClosingAliasWithoutMutatingSource()
    {
        var source=Source();source.Points.Insert(2,source.Points[1]);
        source.Notches=[new NotchMark { VertexIndex=2,Position=source.Points[2] },new NotchMark { VertexIndex=5,Position=source.Points[5] }];
        source.Grainline=new Grainline { Start=new(BitConverter.Int64BitsToDouble(long.MinValue),1),End=new(1,8) };var before=source.Clone();
        var result=PatternTools.RemoveConsecutiveDuplicateVertices(source);
        Assert.Equal(new[] { 1,0 },result.Notches!.Select(mark=>mark.VertexIndex));
        Assert.Equal(source.Notches.Select(mark=>mark.Position),result.Notches.Select(mark=>mark.Position));
        Assert.True(result.Grainline!.NeedsReview);Assert.Equal(source.Grainline.Start,result.Grainline.Start);Assert.Equal(source.Grainline.End,result.Grainline.End);
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(result.Grainline.Start.X));
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
    }
    [Fact]
    public void MarkedCleanupRejectsCollisionsAndSignedZeroDisagreements()
    {
        var source=Source();source.Notches=[new NotchMark { VertexIndex=0,Position=source.Points[0] },new NotchMark { VertexIndex=4,Position=source.Points[4] }];
        Assert.Throws<ArgumentException>(()=>PatternTools.RemoveConsecutiveDuplicateVertices(source));
        source=Source();source.Notches=[new NotchMark { VertexIndex=4,Position=new Point2(BitConverter.Int64BitsToDouble(long.MinValue),0) }];
        Assert.Throws<ArgumentException>(()=>PatternTools.RemoveConsecutiveDuplicateVertices(source));
        source=Source();source.Points[0]=new Point2(BitConverter.Int64BitsToDouble(long.MinValue),0);
        source.Notches=[new NotchMark { VertexIndex=4,Position=source.Points[4] }];
        Assert.Throws<ArgumentException>(()=>PatternTools.RemoveConsecutiveDuplicateVertices(source));
    }
    [Fact]
    public void CancellationPreservesOriginal()
    {
        var source=Source();var before=source.Clone();using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>PatternTools.RemoveExtraClosingVertices(source,cancelled.Token));
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
    }
}