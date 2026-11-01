using TekstilCAD.Core;
using TekstilCAD.Patterns;
namespace TekstilCAD.Tests;
public class OpenPathSplitTests
{
    private static PatternPiece Source() => new() { Closed=false,Points=[new(0,0),new(10,0),new(20,10),new(30,10)],
        Metadata=new PieceMetadata { Quantity=3 },Grainline=new Grainline { Start=new(1,1),End=new(5,1) },
        Notches=[new NotchMark { VertexIndex=1,Position=new(10,0) },new NotchMark { VertexIndex=3,Position=new(30,10) }] };
    [Fact]
    public void SplitRetainsSourceAndDuplicatesTheSharedMarker()
    {
        var source=Source();var before=source.Clone();var (a,b)=OpenPathSplit.Split(source,1);
        Assert.Equal(source.Points.Take(2),a.Points);Assert.Equal(source.Points.Skip(1),b.Points);
        Assert.Equal(1,a.Notches!.Single().VertexIndex);Assert.Equal(new[] { 0,2 },b.Notches!.Select(mark=>mark.VertexIndex));
        Assert.NotSame(source.Notches![0],a.Notches[0]);Assert.Null(a.Metadata);Assert.Null(b.Grainline);
        a.Points[0]=new(99,99);a.Notches[0].Position=new(88,88);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[source] },new ProjectDocument { Pieces=[before] }));
    }
    [Fact]
    public void RejectsEndpointsInvalidMarksAndCancellation()
    {
        var source=Source();Assert.Throws<ArgumentException>(()=>OpenPathSplit.Split(source,0));
        Assert.Throws<ArgumentException>(()=>OpenPathSplit.Split(source,source.Points.Count-1));
        source.Notches![0].Position=new(9,0);Assert.Throws<ArgumentException>(()=>OpenPathSplit.Split(source,1));
        using var cancel=new CancellationTokenSource();cancel.Cancel();Assert.Throws<OperationCanceledException>(()=>OpenPathSplit.Split(source,1,cancel.Token));
    }
    [Fact]
    public void PreservesMarkerSignedZeroOnBothCopies()
    {
        var z=BitConverter.Int64BitsToDouble(long.MinValue);var source=Source();source.Notches![0].Position=new Point2(10,z);
        var (a,b)=OpenPathSplit.Split(source,1);
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(a.Notches!.Single().Position.Y));
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(b.Notches![0].Position.Y));
    }
    [Fact]
    public void RejectsOversizedSourceBeforeCreatingResults()
    {
        var source=new PatternPiece { Closed=false,Points=Enumerable.Range(0,10001).Select(i=>new Point2(i,0)).ToList() };
        Assert.Throws<ArgumentException>(()=>OpenPathSplit.Split(source,1));
        Assert.Equal(10001,source.Points.Count);
    }
}
