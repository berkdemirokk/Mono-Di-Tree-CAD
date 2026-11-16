using TekstilCAD.Core;
using TekstilCAD.Core.Commands;
using static TekstilCAD.Tests.PlannerTestData;

namespace TekstilCAD.Tests;

public class PieceStampTests
{
    private static (ProjectDocument Doc, DocumentView View, Selection Selection, PieceStamp Stamp) Setup()
    {
        var doc = B1();
        var view = View(doc);
        var selection = Sel(doc);
        return (doc, view, selection, PieceStamp.Capture(view, selection, doc.Pieces[0].Id, StampScope.Piece));
    }

    [Fact]
    public void AFreshStampIsCurrent()
    {
        var (_, view, selection, stamp) = Setup();
        Assert.Equal(StampState.Current, stamp.Check(view, selection));
        Assert.True(stamp.IsCurrent(view, null));
        Assert.Equal(64, stamp.ContentDigest.Length);
    }

    [Fact]
    public void TheDigestIsDeterministicAndFollowsContentNotIdentity()
    {
        var (doc, view, selection, stamp) = Setup();
        Assert.Equal(stamp.ContentDigest, PieceStamp.Capture(view, selection, doc.Pieces[0].Id, StampScope.Piece).ContentDigest);
        var copy = doc.Clone();
        Assert.Equal(stamp.ContentDigest, PieceStamp.Capture(new DocumentView(copy, Epoch), selection, copy.Pieces[0].Id, StampScope.Piece).ContentDigest);
    }

    [Fact]
    public void AnotherDocumentObjectIsStaleEvenWithEqualContent()
    {
        var (doc, _, selection, stamp) = Setup();
        Assert.Equal(StampState.DifferentDocument, stamp.Check(new DocumentView(doc.Clone(), Epoch), selection));
    }

    [Fact]
    public void ANewProjectEpochIsStale()
    {
        var (doc, _, selection, stamp) = Setup();
        Assert.Equal(StampState.EpochChanged, stamp.Check(new DocumentView(doc, Guid.NewGuid()), selection));
    }

    [Fact]
    public void ARemovedPieceIsStale()
    {
        var (doc, view, selection, stamp) = Setup();
        doc.Pieces.RemoveAt(0);
        Assert.Equal(StampState.PieceMissing, stamp.Check(view, selection));
    }

    [Fact]
    public void AChangedSelectionIsStaleOnlyWhenTheSelectionIsGiven()
    {
        var (doc, view, _, stamp) = Setup();
        Assert.Equal(StampState.SelectionChanged, stamp.Check(view, Sel(doc, 1)));
        Assert.Equal(StampState.SelectionChanged, stamp.Check(view, Selection.None));
        Assert.Equal(StampState.Current, stamp.Check(view, null));
    }

    [Fact]
    public void EveryKindOfChangeToTheTargetPieceIsStale()
    {
        void Stale(Action<ProjectDocument> change)
        {
            var (doc, view, selection, stamp) = Setup();
            change(doc);
            Assert.Equal(StampState.ContentChanged, stamp.Check(view, selection));
        }
        Stale(d => d.Pieces[0].Name = "Başka");
        Stale(d => d.Pieces[0].Points[1] = new Point2(500.5, 20));
        Stale(d => d.Pieces[0].Points.Add(new Point2(0, 0)));
        Stale(d => d.Pieces[0].Closed = false);
        Stale(d => d.Pieces[0].LayerId = Guid.NewGuid());
        Stale(d => d.Pieces[0].Grainline = new Grainline { Start = new(1, 1), End = new(1, 9) });
        Stale(d => d.Pieces[0].Notches = [new NotchMark { VertexIndex = 0, Position = d.Pieces[0].Points[0] }]);
        Stale(d => d.Pieces[0].Metadata = new PieceMetadata { Quantity = 2 });
        Stale(d => d.Layers = [new Layer { Id = Layer.DefaultId, Name = "Katman 1", Locked = true }]);
        Stale(d => d.Layers = [new Layer { Id = Layer.DefaultId, Name = "Katman 1", Visible = false }]);
        Stale(d => d.GradingRules.Add(new GradingRuleSet { SourcePieceId = d.Pieces[0].Id, SourceClosed = true, SourcePoints = [.. d.Pieces[0].Points], Deltas = d.Pieces[0].Points.Select(_ => new Point2(0, 0)).ToList() }));
        Stale(d => d.Pieces[0].Curves = PieceOperationCurveTests.CurvedPiece("x").Curves);
    }

    [Fact]
    public void ChangesToOtherPiecesDoNotMakeAPieceStampStale()
    {
        var (doc, view, selection, stamp) = Setup();
        doc.Pieces[1].Name = "Başka";
        doc.Pieces[2].Points[0] = new Point2(9, 9);
        doc.Name = "Yeni ad";
        Assert.Equal(StampState.Current, stamp.Check(view, selection));
    }

    [Fact]
    public void ADocumentStampNoticesEveryPiece()
    {
        var doc = B1();
        var view = View(doc);
        var stamp = PieceStamp.Capture(view, Selection.None, null, StampScope.Document);
        Assert.True(stamp.IsCurrent(view, null));
        doc.Pieces[2].Points[0] = new Point2(9, 9);
        Assert.Equal(StampState.ContentChanged, stamp.Check(view, null));
        doc.Pieces[2].Points[0] = new Point2(1020, 20);
        Assert.True(stamp.IsCurrent(view, null));
        doc.Pieces.Add(Piece("Yeni", 0, 0, 1, 1));
        Assert.Equal(StampState.ContentChanged, stamp.Check(view, null));
    }

    [Fact]
    public void AStampWithoutContentOnlyWatchesDocumentAndEpoch()
    {
        var doc = B1();
        var view = View(doc);
        var stamp = PieceStamp.Capture(view, Selection.None, null, StampScope.None);
        Assert.Equal("", stamp.ContentDigest);
        doc.Pieces.Clear();
        Assert.True(stamp.IsCurrent(view, null));
        Assert.Equal(StampState.EpochChanged, stamp.Check(new DocumentView(doc, Guid.NewGuid()), null));
    }

    [Fact]
    public void ApplyingAPlanMakesItsOwnStampStale()
    {
        var doc = PieceOperationTestData.Doc(Piece("Parça 1", 20, 20, 120, 80));
        var view = View(doc);
        var plan = Ok(CommandPlanner.Plan(new ResizeCommand(95, null), view, Sel(doc)));
        Assert.True(plan.Stamp.IsCurrent(view, Sel(doc)));
        plan.Apply(doc);
        Assert.Equal(StampState.ContentChanged, plan.Stamp.Check(view, Sel(doc)));
    }

    [Fact]
    public void PlansCarryDocumentReferenceEpochPieceAndDigest()
    {
        var doc = B1();
        var plan = Ok(Plan(new MoveByCommand(25, 0), doc, Sel(doc)));
        Assert.Equal(Epoch, plan.Stamp.ProjectEpoch);
        Assert.Equal(doc.Pieces[0].Id, plan.Stamp.PieceId);
        Assert.True(plan.Stamp.IsSameDocument(doc));
        Assert.Equal(StampScope.Piece, plan.Stamp.Scope);
        Assert.Equal([doc.Pieces[0].Id], plan.Stamp.SelectedIds);
        Assert.Equal(StampScope.Document, Ok(Plan(new RectangleCommand(10, 10), doc)).Stamp.Scope);
    }
}
