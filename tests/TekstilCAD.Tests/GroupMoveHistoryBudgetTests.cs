using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class GroupMoveHistoryBudgetTests
{
    [Fact]
    public void SnapshotBudgetIncludesGeometryAndBothGradingArraysAtExactBoundary()
    {
        var p=new PatternPiece { Points=[new(0,0),new(1,0),new(0,1)] };
        var doc=new ProjectDocument { Pieces=[p],GradingRules=[new GradingRuleSet { SourcePieceId=p.Id,SourceClosed=true,SourcePoints=[..p.Points],Deltas=[new(0,0),new(0,0),new(0,0)] }] };
        ProjectStorage.Validate(doc);
        var exact=new DocumentSession(doc,maximumPoints:9);
        Assert.True(exact.CanRetainCurrentUndoSnapshot);
        Assert.True(exact.Execute(d=>d.Name="after"));
        Assert.True(exact.Undo());
        Assert.True(DocumentSession.ContentEquals(doc,exact.Document));
        var over=new DocumentSession(doc,maximumPoints:8);
        Assert.False(over.CanRetainCurrentUndoSnapshot);
        Assert.Equal(0,over.CapacityDiscardedSnapshots);
        Assert.True(DocumentSession.ContentEquals(doc,over.Document));
    }

    [Fact]
    public void OversizeQueryDoesNotClearExistingHistory()
    {
        var doc=new ProjectDocument { Pieces=[new PatternPiece { Points=[new(0,0),new(1,0),new(0,1)] }] };
        var session=new DocumentSession(doc,maximumPoints:3);
        Assert.True(session.Execute(d=>d.Pieces[0].Points.Add(new(2,2))));
        Assert.False(session.CanRetainCurrentUndoSnapshot);
        Assert.Equal(1,session.UndoCount);
        Assert.Equal(0,session.CapacityDiscardedSnapshots);
        Assert.True(session.Undo());
        Assert.True(DocumentSession.ContentEquals(doc,session.Document));
    }

    [Fact]
    public void MaximumValidGeometryAndGradingFitDefaultBudget()
    {
        var doc=new ProjectDocument();
        for(var i=0;i<20;i++)
        {
            var piece=new PatternPiece { Closed=false,Points=Enumerable.Range(0,10000).Select(n=>new Point2(n,i)).ToList() };
            doc.Pieces.Add(piece);
            doc.GradingRules.Add(new GradingRuleSet { SourcePieceId=piece.Id,SourceClosed=false,SourcePoints=[..piece.Points],Deltas=Enumerable.Repeat(new Point2(0,0),piece.Points.Count).ToList() });
        }
        ProjectStorage.Validate(doc);
        var session=new DocumentSession(doc);
        Assert.True(session.CanRetainCurrentUndoSnapshot);
        Assert.True(session.Execute(d=>d.Name="changed"));
        Assert.Equal(600000,session.StoredPointCount);
        Assert.True(session.Undo());
        Assert.True(DocumentSession.ContentEquals(doc,session.Document));
    }
}
