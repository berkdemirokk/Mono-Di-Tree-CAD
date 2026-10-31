using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class NotchVertexEditTests
{
    private static PatternPiece Source()=>new() { Name="Kalıp",Points=[new(0,0),new(10,0),new(10,10),new(0,10),new(0,0)],
        Metadata=new PieceMetadata { Size="M" },Notches=[new NotchMark { VertexIndex=0,Position=new(0,0) },new NotchMark { VertexIndex=2,Position=new(10,10) }] };
    [Theory]
    [InlineData(0)] [InlineData(4)]
    public void InsertAfterFirstOrClosingAliasReindexesWithoutMovingExistingMarkers(int index)
    {
        var p=Source();var original=p.Clone();var result=PatternTools.InsertPoint(p,index,new Point2(5,0));
        Assert.Equal(new[] { 0,3 },result.Notches!.Select(n=>n.VertexIndex));
        Assert.Equal(new[] { new Point2(0,0),new Point2(10,10) },result.Notches.Select(n=>n.Position));
        Assert.NotSame(p.Notches,result.Notches);Assert.NotSame(p.Notches![0],result.Notches[0]);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[p] },new ProjectDocument { Pieces=[original] }));
    }
    [Fact]
    public void DeleteUnmarkedVertexReindexesAndMarkedVertexIsRejected()
    {
        var p=Source();var result=PatternTools.DeletePoint(p,1);
        Assert.Equal(new[] { 0,1 },result.Notches!.Select(n=>n.VertexIndex));
        Assert.Equal(new Point2(10,10),result.Notches[1].Position);Assert.Equal(result.Points[0],result.Points[^1]);
        Assert.Throws<ArgumentException>(()=>PatternTools.DeletePoint(p,2));
        Assert.Throws<ArgumentException>(()=>PatternTools.DeletePoint(p,0));
        Assert.Throws<ArgumentException>(()=>PatternTools.DeletePoint(p,4));
        Assert.Equal(5,p.Points.Count);
    }
    [Fact]
    public void DeletingUnmarkedFirstAndRepeatedAliasUpdatesAllRemainingIndices()
    {
        var p=Source();p.Notches=[new NotchMark { VertexIndex=2,Position=new(10,10) }];
        var a=PatternTools.DeletePoint(p,0);var b=PatternTools.DeletePoint(p,4);
        Assert.Equal(1,a.Notches![0].VertexIndex);Assert.Equal(new Point2(10,10),a.Notches[0].Position);
        Assert.Equal(a.Points,b.Points);Assert.Equal(a.Notches[0].VertexIndex,b.Notches![0].VertexIndex);
        Assert.Equal(a.Points[0],a.Points[^1]);
    }
    [Theory]
    [InlineData(0)] [InlineData(4)]
    public void MovingClosingAliasMovesCanonicalMarkerWithBothEndpoints(int index)
    {
        var p=Source();var result=PatternTools.MovePoint(p,index,new Point2(-1,-1));
        Assert.Equal(new Point2(-1,-1),result.Points[0]);Assert.Equal(result.Points[0],result.Points[^1]);
        Assert.Equal(0,result.Notches![0].VertexIndex);Assert.Equal(result.Points[0],result.Notches[0].Position);
        Assert.Equal(new Point2(0,0),p.Notches![0].Position);
    }
    [Fact]
    public void InvalidStaleDuplicateOrAliasMarkersNeverProduceResult()
    {
        var p=Source();p.Notches![0].Position=new(1,0);
        Assert.Throws<ArgumentException>(()=>PatternTools.MovePoint(p,1,new(11,0)));
        p=Source();p.Notches!.Add(p.Notches[0].Clone());Assert.Throws<ArgumentException>(()=>PatternTools.InsertPoint(p,1,new(10,5)));
        p=Source();p.Notches=[new NotchMark { VertexIndex=4,Position=p.Points[4] }];Assert.Throws<ArgumentException>(()=>PatternTools.DeletePoint(p,1));
    }
    [Fact]
    public void InvalidGeometryAndPointBudgetLeaveInputUntouched()
    {
        var p=Source();var before=p.Clone();Assert.ThrowsAny<ArgumentException>(()=>PatternTools.MovePoint(p,1,new Point2(0,0)));
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[p] }));
        var large=new PatternPiece { Closed=false,Points=Enumerable.Range(0,10000).Select(i=>new Point2(i,0)).ToList(),
            Notches=[new NotchMark { VertexIndex=0,Position=new(0,0) }] };
        Assert.Throws<ArgumentException>(()=>PatternTools.InsertPoint(large,0,new Point2(.5,0)));Assert.Equal(10000,large.Points.Count);
    }
    [Fact]
    public void CommitRequiresCurrentSnapshotAndUndoRestoresOriginal()
    {
        var p=Source();var rule=new GradingRuleSet { SourcePieceId=p.Id,SourceClosed=true,SourcePoints=[..p.Points],Deltas=Enumerable.Repeat(new Point2(0,0),p.Points.Count).ToList() };
        var session=new DocumentSession(new ProjectDocument { Pieces=[p],GradingRules=[rule] });var before=session.Document.Clone();
        var snapshot=session.Document.Clone();var edited=PatternTools.InsertPoint(snapshot.Pieces[0],0,new Point2(5,0));
        edited.Id=p.Id;edited.Metadata=p.Metadata!.Clone();
        Assert.True(DocumentSession.ContentEquals(snapshot,session.Document));
        Assert.True(session.Execute(d=>d.Pieces[0]=edited,ProjectStorage.Validate));
        Assert.Equal(rule.SourcePoints,session.Document.GradingRules[0].SourcePoints);
        Assert.NotEqual(session.Document.Pieces[0].Points.Count,session.Document.GradingRules[0].SourcePoints.Count);
        Assert.True(session.Undo());Assert.True(DocumentSession.ContentEquals(before,session.Document));
        Assert.True(session.Redo());Assert.Equal(3,session.Document.Pieces[0].Notches![1].VertexIndex);
        session.Execute(d=>d.Name="Başka değişiklik",ProjectStorage.Validate);
        Assert.False(DocumentSession.ContentEquals(snapshot,session.Document));
    }
}
