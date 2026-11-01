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
    public void ConsecutiveCleanupPreservesOtherPointsAndRejectsBitDistinctOrMarkedCopies()
    {
        var source=Source();source.Points.Insert(2,source.Points[1]);var before=source.Clone();
        var edited=PatternTools.RemoveConsecutiveDuplicateVertices(source);
        Assert.Equal(5,edited.Points.Count);Assert.Equal(new Point2(10,0),edited.Points[1]);Assert.Equal(new Point2(10,10),edited.Points[2]);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
        source.Notches=[new NotchMark { VertexIndex=1,Position=source.Points[1] }];
        Assert.Throws<ArgumentException>(()=>PatternTools.RemoveConsecutiveDuplicateVertices(source));
        source=Source();source.Points[^1]=new Point2(BitConverter.Int64BitsToDouble(long.MinValue),0);
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