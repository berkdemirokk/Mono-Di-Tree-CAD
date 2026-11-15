using TekstilCAD.Core;
using TekstilCAD.Core.Commands;
using TekstilCAD.FileFormats;
using static TekstilCAD.Tests.PlannerTestData;

namespace TekstilCAD.Tests;

/// <summary>Plan.Apply for the edits that are pure in Core: the result equals the expected document, validates and undoes in one step.</summary>
public class PlannerApplyTests
{
    private static ProjectDocument Run(ProjectDocument doc, Command command, Selection? selection, out Plan plan)
    {
        var session = new DocumentSession(doc);
        var view = new DocumentView(session.Document, Epoch);
        plan = Ok(CommandPlanner.Plan(command, view, selection ?? Selection.None));
        var before = session.Document.Clone();
        Assert.True(plan.AppliesInCore);
        Assert.True(session.Execute(plan.Apply, ProjectStorage.Validate));
        ProjectStorage.Validate(session.Document);
        Assert.True(DocumentSession.ContentEquals(plan.EditedDocument()!, session.Document));
        Assert.Equal(1, session.UndoCount);
        Assert.True(session.Undo());
        Assert.True(DocumentSession.ContentEquals(before, session.Document));
        Assert.True(session.Redo());
        return session.Document;
    }

    [Fact]
    public void RectangleAppliesAsOneUndoStep()
    {
        var doc = Run(B1(), new RectangleCommand(120, 80), null, out var plan);
        var created = doc.Pieces.Single(p => p.Id == plan.CreatedPieceId);
        Assert.Equal(4, doc.Pieces.Count);
        Assert.Equal([new Point2(1240, 20), new Point2(1360, 20), new Point2(1360, 100), new Point2(1240, 100)], created.Points);
        Assert.True(created.Closed);
        Assert.True(plan.AddsPiece);
        Assert.Equal("Dikdörtgen çizildi: 120 × 80 mm.", plan.ResultText);
    }

    [Fact]
    public void FirstRectangleGoesToTwentyTwenty()
    {
        var doc = Run(new ProjectDocument(), new RectangleCommand(100, 100, IsSquare: true), null, out _);
        Assert.Equal([new Point2(20, 20), new Point2(120, 20), new Point2(120, 120), new Point2(20, 120)], doc.Pieces.Single().Points);
    }

    [Fact]
    public void HorizontalAndVerticalLines()
    {
        var horizontal = Run(new ProjectDocument(), new LineCommand(150, LineOrientation.Horizontal), null, out _).Pieces.Single();
        Assert.Equal([new Point2(20, 20), new Point2(170, 20)], horizontal.Points);
        Assert.False(horizontal.Closed);
        var vertical = Run(new ProjectDocument(), new LineCommand(80, LineOrientation.Vertical), null, out var plan).Pieces.Single();
        Assert.Equal([new Point2(20, 20), new Point2(20, 100)], vertical.Points);
        Assert.False(vertical.Closed);
        Assert.Equal(plan.CreatedPieceId, vertical.Id);
    }

    [Fact]
    public void NewPiecesGoToTheChosenLayer()
    {
        var layer = new Layer { Name = "Ek" };
        var doc = new ProjectDocument { Layers = [new Layer { Id = Layer.DefaultId, Name = "Katman 1" }, layer] };
        var view = new DocumentView(doc, Epoch) { NewPieceLayerId = layer.Id };
        var plan = Ok(CommandPlanner.Plan(new RectangleCommand(10, 10), view, Selection.None));
        plan.Apply(doc);
        Assert.Equal(layer.Id, doc.Pieces.Single().LayerId);
    }

    [Fact]
    public void ResizeKeepsTheTopLeftCorner()
    {
        var doc = PieceOperationTestData.Doc(Piece("Parça 1", 20, 20, 120, 80));
        var result = Run(doc, new ResizeCommand(95, 60), Sel(doc), out _);
        Assert.Equal([new Point2(20, 20), new Point2(115, 20), new Point2(115, 80), new Point2(20, 80)], result.Pieces[0].Points);
    }

    [Fact]
    public void RenameAndDelete()
    {
        var doc = B1();
        var renamed = Run(doc, new RenameCommand("Ön Beden 2"), Sel(doc), out _);
        Assert.Equal("Ön Beden 2", renamed.Pieces[0].Name);
        var deleted = Run(B1(), new DeleteCommand(PieceTarget.Named, "kol"), null, out var plan);
        Assert.Equal(["Ön beden", "Arka beden"], deleted.Pieces.Select(p => p.Name));
        Assert.Equal("‘Kol’ silindi.", plan.ResultText);
    }

    [Fact]
    public void DeleteRemovesTheGradingRuleToo()
    {
        var doc = B1();
        var piece = doc.Pieces[2];
        doc.GradingRules.Add(new GradingRuleSet { SourcePieceId = piece.Id, SourceClosed = true, SourcePoints = [.. piece.Points], Deltas = piece.Points.Select(_ => new Point2(0, 0)).ToList() });
        var plan = Ok(Plan(new DeleteCommand(PieceTarget.Selected), doc, Sel(doc, 2)));
        Assert.Equal(["Parçaya bağlı 1 serileme kuralı da silinir."], plan.Caveats);
        plan.Apply(doc);
        Assert.Empty(doc.GradingRules);
    }

    [Fact]
    public void DuplicateAddsACopyNextToTheOriginal()
    {
        var doc = B1();
        var session = new DocumentSession(doc);
        var plan = Ok(CommandPlanner.PlanDuplicate(new DocumentView(session.Document, Epoch), Sel(doc)));
        Assert.True(session.Execute(plan.Apply, ProjectStorage.Validate));
        doc = session.Document;
        Assert.Equal(4, doc.Pieces.Count);
        var copy = doc.Pieces.Single(p => p.Id == plan.CreatedPieceId);
        Assert.Equal("Ön beden kopya", copy.Name);
        Assert.Equal(new Point2(40, 40), copy.Points[0]);
    }

    [Fact]
    public void ApplyRefusesAChangedDocument()
    {
        var doc = B1();
        var plan = Ok(Plan(new RenameCommand("X"), doc, Sel(doc)));
        doc.Pieces[1].Name = "Başka";
        var ex = Assert.Throws<PlanStaleException>(() => plan.Apply(doc));
        Assert.Equal(Refusals.TemplatePreviewStale, ex.Message);
        Assert.Equal("Ön beden", doc.Pieces[0].Name);
    }

    [Fact]
    public void ApplyingTwiceIsRefused()
    {
        var doc = B1();
        var plan = Ok(Plan(new DeleteCommand(PieceTarget.Named, "kol"), doc));
        plan.Apply(doc);
        Assert.Throws<PlanStaleException>(() => plan.Apply(doc));
        Assert.Equal(2, doc.Pieces.Count);
    }

    [Fact]
    public void ThePlanKeepsItsOwnCopyWhenTheCallerKeepsEditing()
    {
        var doc = B1();
        var plan = Ok(Plan(new RenameCommand("X"), doc, Sel(doc)));
        var edited = plan.EditedDocument()!;
        edited.Pieces.Clear();
        Assert.Equal(3, plan.EditedDocument()!.Pieces.Count);
    }

    [Fact]
    public void MoveRotateAndMirrorAreAppliedByTheDesktop()
    {
        var doc = B1();
        Plan[] plans =
        [
            Ok(Plan(new MoveByCommand(25, 0), doc, Sel(doc))),
            Ok(Plan(new MoveToCommand(5, 5), doc, Sel(doc))),
            Ok(Plan(new RotateCommand(90, RotationDirection.Clockwise), doc, Sel(doc))),
            Ok(Plan(new MirrorCommand(MirrorAxis.Vertical), doc, Sel(doc))),
        ];
        foreach (var plan in plans)
        {
            Assert.False(plan.AppliesInCore);
            Assert.NotNull(plan.Operation);
            Assert.True(plan.NeedsSelectedPiece);
            Assert.Null(plan.EditedDocument());
            Assert.Throws<InvalidOperationException>(() => plan.Apply(doc));
        }
        Assert.Equal(new MovePieceOperation(doc.Pieces[0].Id, 25, 0), plans[0].Operation);
        Assert.Equal(new MovePieceToOperation(doc.Pieces[0].Id, 5, 5, -15, -15), plans[1].Operation);
        Assert.Equal(new RotatePieceOperation(doc.Pieces[0].Id, 90, "rotate-cw"), plans[2].Operation);
    }

    [Fact]
    public void MovePlansListWhatTravelsWithThePiece()
    {
        var doc = B1();
        var piece = doc.Pieces[0];
        piece.Grainline = new Grainline { Start = new(200, 100), End = new(200, 500) };
        piece.Notches = [new NotchMark { VertexIndex = 1, Position = piece.Points[1] }];
        doc.GradingRules.Add(new GradingRuleSet { SourcePieceId = piece.Id, SourceClosed = true, SourcePoints = [.. piece.Points], Deltas = piece.Points.Select(_ => new Point2(0, 0)).ToList() });
        var move = Ok(Plan(new MoveByCommand(25, 0), doc, Sel(doc)));
        Assert.Equal(["İplik yönü, çentik işaretleri ve serileme kuralı parçayla birlikte taşınır."], move.Caveats);
        Assert.Equal("İplik yönü, çentik işaretleri ve serileme kuralı parçayla birlikte döner.",
            Ok(Plan(new RotateCommand(90, RotationDirection.Clockwise), doc, Sel(doc))).Caveats[0]);
        var plain = B1();
        Assert.Empty(Ok(Plan(new MoveByCommand(25, 0), plain, Sel(plain))).Caveats);
    }
}
