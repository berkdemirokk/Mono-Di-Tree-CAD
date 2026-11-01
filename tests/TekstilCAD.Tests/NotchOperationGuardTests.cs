using TekstilCAD.Core;
using TekstilCAD.Grading;
using TekstilCAD.Nesting;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class NotchOperationGuardTests
{
    private static PatternPiece Closed()=>new() { Points=[new(0,0),new(20,0),new(20,20),new(0,20)],
        Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=1 },
        Notches=[new NotchMark { VertexIndex=1,Position=new(20,0) }] };
    private static PatternPiece Open()=>new() { Closed=false,Points=[new(0,0),new(10,0),new(10,10)],
        Notches=[new NotchMark { VertexIndex=1,Position=new(10,0) }] };
    [Fact]
    public void AllDerivedPatternEditsRejectWithoutChangingSource()
    {
        var p=Closed();var before=p.Clone();
        Action[] operations=[()=>PatternTools.Offset(p,1),()=>PatternTools.OffsetConvex(p,1),
            ()=>PatternTools.DeletePoint(p,1),()=>PatternTools.MovePoint(p,1,new(19,1))];
        foreach(var action in operations) { var ex=Assert.Throws<ArgumentException>(action);Assert.Contains("Çentik",ex.Message); }
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[p] }));
    }
    [Fact]
    public void MovingAnUnmarkedVertexRetainsMarksAndRejectsTheirRemoval()
    {
        var source=Closed();var before=source.Clone();
        var result=PatternTools.MovePoint(source,0,new(-1,0));
        Assert.Equal(new Point2(-1,0),result.Points[0]);
        Assert.Equal(1,result.Notches!.Single().VertexIndex);
        Assert.Equal(source.Points[1],result.Notches.Single().Position);
        Assert.NotSame(source.Notches,result.Notches);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.DeletePoint(source,1));
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>PatternTools.MovePoint(source,0,new(-1,0),cancellation.Token));
    }
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public void MovingUnmarkedRepeatedClosurePreservesExactMarkerZeroBits(int closureIndex,bool negative)
    {
        var zero=negative ? BitConverter.Int64BitsToDouble(long.MinValue) : 0d;
        var source=Closed();source.Points.Add(source.Points[0]);
        source.Notches=[new NotchMark { VertexIndex=1,Position=new Point2(20,zero) },
                        new NotchMark { VertexIndex=3,Position=new Point2(zero,20) }];
        var result=PatternTools.MovePoint(source,closureIndex,new(-1,0));
        Assert.Equal(new Point2(-1,0),result.Points[0]);Assert.Equal(result.Points[0],result.Points[^1]);
        Assert.Equal(BitConverter.DoubleToInt64Bits(zero),BitConverter.DoubleToInt64Bits(result.Notches![0].Position.Y));
        Assert.Equal(BitConverter.DoubleToInt64Bits(zero),BitConverter.DoubleToInt64Bits(result.Notches[1].Position.X));
        Assert.Equal(BitConverter.DoubleToInt64Bits(zero),BitConverter.DoubleToInt64Bits(source.Notches![0].Position.Y));
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>PatternTools.MovePoint(source,closureIndex,new(-1,0),cancellation.Token));
        Assert.Equal(BitConverter.DoubleToInt64Bits(zero),BitConverter.DoubleToInt64Bits(source.Notches[1].Position.X));
    }
    [Fact]
    public void RepeatedClosingVertexCannotMoveWhenItsFirstVertexIsMarked()
    {
        var source=Closed();source.Points.Add(source.Points[0]);source.Notches=[new NotchMark { VertexIndex=0,Position=source.Points[0] }];
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.MovePoint(source,0,new(-1,0)));
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.MovePoint(source,source.Points.Count-1,new(-1,0)));
    }
    [Theory]
    [InlineData(0,2)]
    [InlineData(1,1)]
    [InlineData(3,1)]
    [InlineData(4,2)]
    public void MarkedInsertionRemapsIndicesIncludingExplicitClosure(int afterIndex,int expectedIndex)
    {
        var source=Closed();source.Points.Add(source.Points[0]);source.Notches![0].Position=new Point2(20,BitConverter.Int64BitsToDouble(long.MinValue));var before=source.Clone();
        var newPoint=afterIndex is 0 or 4 ? new Point2(10,0) : afterIndex==1 ? new Point2(20,10) : new Point2(0,10);
        var result=PatternTools.InsertPoint(source,afterIndex,newPoint);
        Assert.Equal(expectedIndex,result.Notches!.Single().VertexIndex);
        Assert.Equal(source.Notches!.Single().Position,result.Notches.Single().Position);
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(result.Notches.Single().Position.Y));
        Assert.Equal(result.Points[result.Notches.Single().VertexIndex],result.Notches.Single().Position);
        Assert.Equal(result.Points[0],result.Points[^1]);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
    }
    [Theory]
    [InlineData("midpoint",0)]
    [InlineData("distance",0)]
    [InlineData("percentage",0)]
    [InlineData("midpoint",4)]
    [InlineData("distance",4)]
    [InlineData("percentage",4)]
    public void MarkedEdgeSplitRemapsIndicesAndPreservesMarkerBits(string kind,int after)
    {
        var source=Closed();source.Points.Add(source.Points[0]);source.Notches![0].Position=new Point2(20,BitConverter.Int64BitsToDouble(long.MinValue));
        var before=source.Clone();
        PatternPiece Split(CancellationToken token=default)=>kind switch {
            "midpoint"=>PatternTools.InsertEdgeMidpoint(source,after,token),
            "distance"=>PatternTools.InsertPointAtDistance(source,after,10,token),
            _=>PatternTools.InsertPointAtPercentage(source,after,50,token) };
        var result=Split();var mark=Assert.Single(result.Notches!);
        Assert.Equal(new Point2(10,0),result.Points[1]);Assert.Equal(2,mark.VertexIndex);
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(mark.Position.Y));Assert.Equal(result.Points[2],mark.Position);
        Assert.Equal(result.Points[0],result.Points[^1]);
        using var canceled=new CancellationTokenSource();canceled.Cancel();Assert.Throws<OperationCanceledException>(()=>Split(canceled.Token));
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
    }
    [Fact]
    public void MarkedEdgeSplitRejectsInvalidParametersAndUnrepresentableMidpoint()
    {
        var source=Open();Assert.ThrowsAny<ArgumentException>(()=>PatternTools.InsertPointAtDistance(source,0,0));
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.InsertPointAtPercentage(source,0,100));
        var tiny=new PatternPiece { Closed=false,Points=[new(0,0),new(double.Epsilon,0)],Notches=[new NotchMark { VertexIndex=1,Position=new(double.Epsilon,0) }] };
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.InsertEdgeMidpoint(tiny,0));
    }
    [Fact]
    public void MarkedOpenInsertionAndInvalidOrCancelledInsertionAreSafe()
    {
        var source=Open();var before=source.Clone();var result=PatternTools.InsertPoint(source,0,new(5,0));
        Assert.Equal(2,result.Notches!.Single().VertexIndex);Assert.Equal(source.Notches!.Single().Position,result.Notches.Single().Position);
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.InsertPoint(source,0,source.Points[0]));
        using var canceled=new CancellationTokenSource();canceled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>PatternTools.InsertPoint(source,0,new(5,0),canceled.Token));
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
    }
    [Theory]
    [InlineData(0,0)]
    [InlineData(2,1)]
    [InlineData(4,0)]
    public void DeleteUnmarkedVertexRemapsMarkersAndRetainsZeroBits(int index,int expectedIndex)
    {
        var source=Closed();source.Points.Add(source.Points[0]);source.Notches![0].Position=new Point2(20,BitConverter.Int64BitsToDouble(long.MinValue));var before=source.Clone();
        var edited=PatternTools.DeletePoint(source,index);var mark=Assert.Single(edited.Notches!);
        Assert.Equal(expectedIndex,mark.VertexIndex);Assert.Equal(edited.Points[expectedIndex],mark.Position);
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(mark.Position.Y));Assert.Equal(edited.Points[0],edited.Points[^1]);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
    }
    [Fact]
    public void DeleteMarkedVertexAndInvalidOrCancelledDeletionAreRejected()
    {
        var source=Open();var before=source.Clone();
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.DeletePoint(source,1));
        var edited=PatternTools.DeletePoint(source,0);Assert.Equal(0,Assert.Single(edited.Notches!).VertexIndex);
        using var canceled=new CancellationTokenSource();canceled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>PatternTools.DeletePoint(source,0,canceled.Token));
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[source] }));
        var closed=new PatternPiece { Points=[new(0,0),new(10,0),new(0,10)],Notches=[new NotchMark { VertexIndex=1,Position=new(10,0) }] };
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.DeletePoint(closed,0));
        var repeated=Closed();repeated.Points.Add(repeated.Points[0]);repeated.Notches=[new NotchMark { VertexIndex=0,Position=repeated.Points[0] }];
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.DeletePoint(repeated,0));
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.DeletePoint(repeated,4));
    }
    [Fact]
    public void GradingAndBasicNestingRetainMarksWhileRotationStillRejects()
    {
        var p=Closed();var original=p.Clone();var increments=Enumerable.Repeat(new Point2(0,0),p.Points.Count).ToArray();
        var graded=GradingEngine.Generate(p,increments,1,"M");
        Assert.Equal(p.Notches![0].VertexIndex,Assert.Single(graded.Notches!).VertexIndex);
        Assert.Equal(p.Notches[0].Position,graded.Notches![0].Position);
        Assert.Empty(GradingEngine.GenerateSizes(p,increments,Array.Empty<GradeSize>()));
        Assert.Throws<ArgumentException>(()=>OrthogonalRotation.Rotate(p,90));
        var layout=NestingEngine.Arrange([p],100).Layout.Pieces.Single();
        Assert.Equal(layout.Points[1],layout.Notches!.Single().Position);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[original] },new ProjectDocument { Pieces=[p] }));
    }
    [Fact]
    public void AddingOrChangingNotchMakesPreviousLayoutFingerprintStale()
    {
        var p=Closed();p.Notches=null;var d=new ProjectDocument { Pieces=[p] };
        var prior=NestingSnapshot.ComputeLayoutFingerprint(d);
        p.Notches=[new NotchMark { VertexIndex=1,Position=p.Points[1] }];
        var withMark=NestingSnapshot.ComputeLayoutFingerprint(d);Assert.NotEqual(prior,withMark);
        p.Notches[0].VertexIndex=2;p.Notches[0].Position=p.Points[2];
        Assert.NotEqual(withMark,NestingSnapshot.ComputeLayoutFingerprint(d));
        p.Notches=null;Assert.Equal(prior,NestingSnapshot.ComputeLayoutFingerprint(d));
    }
}
