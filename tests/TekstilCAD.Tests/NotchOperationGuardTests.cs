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
            ()=>PatternTools.InsertEdgeMidpoint(p,0),()=>PatternTools.DeletePoint(p,0),()=>PatternTools.MovePoint(p,1,new(19,1))];
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
    [Fact]
    public void OpenPathOperationsRejectMarksFromEitherSource()
    {
        var marked=Open();var other=new PatternPiece { Closed=false,Points=[new(10,10),new(15,10)] };
        Assert.Throws<ArgumentException>(()=>PatternTools.ReverseOpenPath(marked));
        Assert.Throws<ArgumentException>(()=>OpenPathClosure.Close(marked,20));
        Assert.Throws<ArgumentException>(()=>OpenPathJoin.Join(marked,other,0));
        Assert.Throws<ArgumentException>(()=>OpenPathJoin.Join(other,marked,0));
        Assert.Single(marked.Notches!);
    }
    [Fact]
    public void GradingAndNestingRejectBeforeCreatingUnmarkedResults()
    {
        var p=Closed();var original=p.Clone();var increments=Enumerable.Repeat(new Point2(0,0),p.Points.Count).ToArray();
        Assert.Throws<ArgumentException>(()=>GradingEngine.Generate(p,increments,1,"M"));
        Assert.Throws<ArgumentException>(()=>GradingEngine.GenerateSizes(p,increments,Array.Empty<GradeSize>()));
        Assert.Throws<ArgumentException>(()=>OrthogonalRotation.Rotate(p,90));
        Assert.Throws<ArgumentException>(()=>NestingEngine.Arrange([p],100));
        Assert.Throws<ArgumentException>(()=>ProductionNestingPlan.Arrange(new ProjectDocument { Pieces=[p] },[p.Id],100));
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
