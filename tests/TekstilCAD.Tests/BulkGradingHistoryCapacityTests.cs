using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class BulkGradingHistoryCapacityTests
{
    private static (ProjectDocument Source,ProjectDocument Result) Documents()
    {
        var sourcePiece=new PatternPiece { Points=[new(0,0),new(10,0),new(0,10)] };
        var source=new ProjectDocument { Pieces=[sourcePiece] };
        var generated=sourcePiece.Clone(); generated.Id=Guid.NewGuid();
        var result=source.Clone(); result.Pieces.Add(generated);
        result.GradingRules.Add(new GradingRuleSet { SourcePieceId=generated.Id,SourceClosed=true,SourcePoints=[..generated.Points],Deltas=[new(0,0),new(1,0),new(0,1)] });
        ProjectStorage.Validate(source);ProjectStorage.Validate(result);
        return (source,result);
    }

    [Fact]
    public void ActualSessionBudgetAccountsForGeneratedRulesAndRedoSnapshot()
    {
        var (source,result)=Documents();
        var exact=new DocumentSession(source,maximumPoints:12);
        Assert.True(exact.CanRetainCurrentUndoSnapshot);
        Assert.True(exact.CanRetainUndoSnapshot(result));
        Assert.True(exact.Execute(d=> { d.Pieces.Add(result.Pieces[1].Clone());d.GradingRules.Add(result.GradingRules[0].Clone()); },ProjectStorage.Validate));
        Assert.True(exact.Undo());Assert.True(DocumentSession.ContentEquals(source,exact.Document));
        Assert.True(exact.Redo());Assert.True(DocumentSession.ContentEquals(result,exact.Document));
        var tooSmall=new DocumentSession(source,maximumPoints:11);
        Assert.True(tooSmall.CanRetainCurrentUndoSnapshot);
        Assert.False(tooSmall.CanRetainUndoSnapshot(result));
        Assert.Equal(0,tooSmall.UndoCount);Assert.Equal(0,tooSmall.CapacityDiscardedSnapshots);
        Assert.True(DocumentSession.ContentEquals(source,tooSmall.Document));
    }

    [Fact]
    public void OversizedSourceSnapshotIsRejectedWithoutChangingHistory()
    {
        var (source,result)=Documents();
        var session=new DocumentSession(source,maximumPoints:2);
        Assert.False(session.CanRetainCurrentUndoSnapshot);
        Assert.False(session.CanRetainUndoSnapshot(result));
        Assert.Equal(0,session.UndoCount);Assert.Equal(0,session.CapacityDiscardedSnapshots);
        Assert.True(DocumentSession.ContentEquals(source,session.Document));
    }
}
