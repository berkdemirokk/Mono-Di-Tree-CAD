using TekstilCAD.Core;
using TekstilCAD.Core.Commands;
using static TekstilCAD.Tests.PlannerTestData;

namespace TekstilCAD.Tests;

/// <summary>Every R-code the planner can produce (FINAL_COMMANDS.md section 3).</summary>
public class PlannerRefusalTests
{
    private static readonly MoveByCommand Move = new(25, 0);

    private static ProjectDocument LockedLayerDocument()
    {
        var doc = B1();
        doc.Layers = [new Layer { Id = Layer.DefaultId, Name = "Katman 1", Locked = true }];
        return doc;
    }

    [Fact]
    public void R03NoSelectionListsPieceNamesAsButtons()
    {
        var doc = B1();
        var refusal = Refused(Plan(Move, doc));
        Assert.Equal("R03", refusal.Code);
        Assert.Equal(Refusals.TemplateNoSelection, refusal.Message);
        Assert.Equal(["Ön beden", "Arka beden", "Kol"], refusal.Buttons.Select(b => b.Label));
        Assert.Equal("Ön beden seç", refusal.Buttons[0].FillText);
    }

    [Fact]
    public void R03NoPiecesAtAll()
    {
        var refusal = Refused(Plan(Move, new ProjectDocument()));
        Assert.Equal("R03", refusal.Code);
        Assert.Equal(Refusals.TemplateNoPieces, refusal.Message);
    }

    [Fact]
    public void R03SelectionOfAMissingPieceCountsAsNothingSelected()
    {
        var doc = B1();
        Assert.Equal("R03", Refused(Plan(Move, doc, Selection.Of(Guid.NewGuid()))).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30000)]
    [InlineData(20001)]
    public void R04SideOutOfRange(double side)
    {
        var refusal = Refused(Plan(new RectangleCommand(side, 80), new ProjectDocument()));
        Assert.Equal("R04", refusal.Code);
        Assert.Equal(Refusals.TemplateOutOfRange, refusal.Message);
        Assert.Equal("R04", Refused(Plan(new LineCommand(side, LineOrientation.Vertical), new ProjectDocument())).Code);
    }

    [Fact]
    public void R07DrawingInProgressBlocksEverythingThatChangesTheProject()
    {
        var doc = B1();
        var view = View(doc, drawing: true);
        var selected = Sel(doc);
        Command[] commands =
        [
            new RectangleCommand(120, 80), new LineCommand(50, LineOrientation.Horizontal), Move, new MoveToCommand(1, 1),
            new RotateCommand(90, RotationDirection.Clockwise), new MirrorCommand(MirrorAxis.Vertical), new ResizeCommand(95, null),
            new RenameCommand("x"), new DeleteCommand(PieceTarget.Selected), new DeleteCommand(PieceTarget.Named, "kol"),
            new SelectCommand(PieceTarget.Named, "kol"), new UndoCommand(), new RedoCommand(), new ToolCommand(DrawingTool.Arc),
        ];
        foreach (var command in commands)
        {
            var result = CommandPlanner.Plan(command, view, selected);
            Assert.Equal("R07", result.Code);
            Assert.Equal(Refusals.TemplateDrawingInProgress, result.Refusal!.Message);
            Assert.Equal(["çizimi kapat", "çizimi iptal et"], result.Refusal.Buttons.Select(b => b.Label));
        }
    }

    [Fact]
    public void R08LockedLayerRefusesEditsAndNewPieces()
    {
        var doc = LockedLayerDocument();
        Command[] edits =
        [
            Move, new MoveToCommand(5, 5), new RotateCommand(90, RotationDirection.Clockwise), new MirrorCommand(MirrorAxis.Horizontal),
            new ResizeCommand(95, null), new RenameCommand("x"), new DeleteCommand(PieceTarget.Selected), new DeleteCommand(PieceTarget.Named, "kol"),
        ];
        foreach (var command in edits)
            Assert.Equal("R08", Plan(command, doc, Sel(doc)).Code);
        var add = Refused(Plan(new RectangleCommand(120, 80), doc));
        Assert.Equal("R08", add.Code);
        Assert.Equal("Ayrıntılı görünüm", add.Buttons.Single().Label);
        Assert.Equal(Refusals.TemplateLockedLayer, Refused(Plan(Move, doc, Sel(doc))).Message);
    }

    [Fact]
    public void R09FileOperationRunning()
    {
        var doc = B1();
        var view = new DocumentView(doc, Epoch) { FileOperationInProgress = true };
        Assert.Equal("R09", CommandPlanner.Plan(new RectangleCommand(120, 80), view, Selection.None).Code);
        Assert.Equal("R09", CommandPlanner.Plan(new FileCommand(FileAction.Save), view, Selection.None).Code);
    }

    [Fact]
    public void R10NotARectangle()
    {
        var triangle = new PatternPiece { Name = "Üçgen", Points = [new(0, 0), new(100, 0), new(50, 80)] };
        var doc = PieceOperationTestData.Doc(triangle);
        var refusal = Refused(Plan(new ResizeCommand(95, null), doc, Sel(doc)));
        Assert.Equal("R10", refusal.Code);
        Assert.Equal(Refusals.TemplateNotRectangle, refusal.Message);
    }

    [Fact]
    public void R11ExtraDataBlocksResize()
    {
        var withGrainline = Piece("Ön beden", 0, 0, 100, 100);
        withGrainline.Grainline = new Grainline { Start = new(50, 10), End = new(50, 90) };
        var docGrain = PieceOperationTestData.Doc(withGrainline);
        Assert.Equal("R11", Plan(new ResizeCommand(95, null), docGrain, Sel(docGrain)).Code);

        var withRule = Piece("Ön beden", 0, 0, 100, 100);
        var docRule = PieceOperationTestData.Doc(withRule);
        docRule.GradingRules.Add(new GradingRuleSet { SourcePieceId = withRule.Id, SourceClosed = true, SourcePoints = [.. withRule.Points], Deltas = withRule.Points.Select(_ => new Point2(0, 0)).ToList() });
        Assert.Equal("R11", Plan(new ResizeCommand(95, null), docRule, Sel(docRule)).Code);

        var withNotch = Piece("Ön beden", 0, 0, 100, 100);
        withNotch.Notches = [new NotchMark { VertexIndex = 0, Position = withNotch.Points[0] }];
        var docNotch = PieceOperationTestData.Doc(withNotch);
        Assert.Equal("R11", Plan(new ResizeCommand(95, null), docNotch, Sel(docNotch)).Code);

        var curved = PieceOperationCurveTests.CurvedPiece("Eğri");
        var docCurve = PieceOperationTestData.Doc(curved);
        var refusal = Refused(Plan(new ResizeCommand(95, null), docCurve, Sel(docCurve)));
        Assert.Equal("R11", refusal.Code);
        Assert.Equal(Refusals.TemplateExtraData, refusal.Message);
    }

    [Fact]
    public void R12CurvedPieceCannotMoveRotateOrMirror()
    {
        var doc = PieceOperationTestData.Doc(PieceOperationCurveTests.CurvedPiece("Eğri"));
        Command[] commands = [Move, new MoveToCommand(5, 5), new RotateCommand(90, RotationDirection.Clockwise), new MirrorCommand(MirrorAxis.Vertical)];
        foreach (var command in commands)
        {
            var refusal = Refused(Plan(command, doc, Sel(doc)));
            Assert.Equal("R12", refusal.Code);
            Assert.Equal(Refusals.TemplateCurvedPiece, refusal.Message);
        }
    }

    [Theory]
    [InlineData(45)]
    [InlineData(0)]
    [InlineData(100)]
    public void R13UnsupportedAngle(int degrees)
    {
        var doc = B1();
        Assert.Equal("R13", Refused(Plan(new RotateCommand(degrees, RotationDirection.Clockwise), doc, Sel(doc))).Code);
    }

    [Fact]
    public void R14SeveralPiecesSelected()
    {
        var doc = B1();
        var two = new Selection([doc.Pieces[0].Id, doc.Pieces[1].Id]);
        var refusal = Refused(Plan(Move, doc, two));
        Assert.Equal("R14", refusal.Code);
        Assert.Equal("seçimi bırak", refusal.Buttons.Single().Label);
    }

    [Fact]
    public void R15aNameNotFoundShowsRealNames()
    {
        var doc = B1();
        var refusal = Refused(Plan(new SelectCommand(PieceTarget.Named, "etek"), doc));
        Assert.Equal("R15a", refusal.Code);
        Assert.Equal("“etek” adında parça bulunamadı. Örnek: Ön beden seç", refusal.Message);
        Assert.Equal("Ön beden seç", refusal.Buttons[0].FillText);
        Assert.Equal("R15a", Refused(Plan(new DeleteCommand(PieceTarget.Named, "etek"), doc)).Code);
    }

    [Fact]
    public void R15bSeveralMatchesAreAChoiceWithOneButtonPerPiece()
    {
        var doc = PieceOperationTestData.Doc(Piece("Ön beden", 0, 0, 480, 620), Piece("Ön beden 2", 500, 0, 480, 620));
        var refusal = Refused(Plan(new SelectCommand(PieceTarget.Named, "ön"), doc));
        Assert.Equal("R15b", refusal.Code);
        Assert.Equal("2 parça eşleşti. Hangisi?", refusal.Message);
        Assert.Equal(["Ön beden · 480 × 620 mm", "Ön beden 2 · 480 × 620 mm"], refusal.Buttons.Select(b => b.Label));
        Assert.Equal(["Ön beden seç", "Ön beden 2 seç"], refusal.Buttons.Select(b => b.FillText));
        Assert.Equal("Ön beden sil", Refused(Plan(new DeleteCommand(PieceTarget.Named, "ön"), doc)).Buttons[0].FillText);
    }

    [Fact]
    public void ExactNameWinsOverLongerNames()
    {
        var doc = PieceOperationTestData.Doc(Piece("Ön beden", 0, 0, 480, 620), Piece("Ön beden 2", 500, 0, 480, 620));
        Assert.Equal(doc.Pieces[0].Id, Ok(Plan(new SelectCommand(PieceTarget.Named, "ön beden"), doc)).TargetPieceId);
        Assert.Equal(doc.Pieces[1].Id, Ok(Plan(new SelectCommand(PieceTarget.Named, "ön beden 2"), doc)).TargetPieceId);
    }

    [Fact]
    public void R17TooManyPointsForRotateAndMirror()
    {
        var points = Enumerable.Range(0, 5000).Select(i => new Point2(Math.Cos(i * 0.00125) * 500, Math.Sin(i * 0.00125) * 500)).ToList();
        var doc = PieceOperationTestData.Doc(new PatternPiece { Name = "Büyük", Points = points });
        Assert.Equal("R17", Plan(new RotateCommand(90, RotationDirection.Clockwise), doc, Sel(doc)).Code);
        Assert.Equal("R17", Plan(new MirrorCommand(MirrorAxis.Vertical), doc, Sel(doc)).Code);
    }

    [Fact]
    public void R18NothingToUndoOrRedo()
    {
        var doc = B1();
        var none = new DocumentView(doc, Epoch);
        var undo = CommandPlanner.Plan(new UndoCommand(), none, Selection.None).Refusal!;
        Assert.Equal("R18", undo.Code);
        Assert.Equal(Refusals.TemplateNothingToUndo, undo.Message);
        var redo = CommandPlanner.Plan(new RedoCommand(), none, Selection.None).Refusal!;
        Assert.Equal("R18", redo.Code);
        Assert.Equal(Refusals.TemplateNothingToRedo, redo.Message);
    }

    [Fact]
    public void R20UnappliedDrafts()
    {
        var doc = B1();
        var view = new DocumentView(doc, Epoch) { HasUnappliedDrafts = true };
        Assert.Equal("R20", CommandPlanner.Plan(Move, view, Sel(doc)).Code);
        Assert.Equal("R20", CommandPlanner.Plan(new RectangleCommand(120, 80), view, Selection.None).Code);
    }

    [Fact]
    public void R21RecoveryIncompleteComesFirst()
    {
        var doc = B1();
        var view = new DocumentView(doc, Epoch) { RecoveryIncomplete = true, FileOperationInProgress = true, DrawingInProgress = true };
        Assert.Equal("R21", CommandPlanner.Plan(Move, view, Sel(doc)).Code);
    }

    [Fact]
    public void R24EmptyName()
    {
        var doc = B1();
        Assert.Equal("R24", Refused(Plan(new RenameCommand("   "), doc, Sel(doc))).Code);
        Assert.Equal("R24", Refused(Plan(new RenameCommand(new string('a', 129)), doc, Sel(doc))).Code);
    }

    [Fact]
    public void R25NothingChanges()
    {
        var doc = PieceOperationTestData.Doc(Piece("Parça 1", 20, 20, 120, 80));
        var same = Refused(Plan(new ResizeCommand(120, 80), doc, Sel(doc)));
        Assert.Equal("R25", same.Code);
        Assert.Equal("Ölçü zaten 120 × 80 mm. Değişiklik yok.", same.Message);
        Assert.Equal("R25", Plan(new RenameCommand("Parça 1"), doc, Sel(doc)).Code);
        Assert.Equal("R25", Plan(new MoveByCommand(0, 0), doc, Sel(doc)).Code);
        Assert.Equal("R25", Plan(new MoveToCommand(20, 20), doc, Sel(doc)).Code);
    }

    [Fact]
    public void R02ResizeWithoutAnyMeasure()
    {
        var doc = PieceOperationTestData.Doc(Piece("Parça 1", 20, 20, 120, 80));
        Assert.Equal("R02", Refused(Plan(new ResizeCommand(null, null), doc, Sel(doc))).Code);
    }

    [Fact]
    public void ParserRefusalsPassThroughUnchanged()
    {
        var doc = B1();
        var parsed = CommandParser.Parse("12 x 8 dikdörtgen", new ParserContext(["Ön beden"]));
        var planned = CommandPlanner.Plan(parsed, View(doc), Selection.None);
        Assert.Same(parsed.Notice, planned.Refusal);
        Assert.Equal("R06", planned.Code);
    }

    [Fact]
    public void NoRefusalNamesAForbiddenWord()
    {
        var doc = B1();
        var results = new[]
        {
            Plan(Move, doc), Plan(new SelectCommand(PieceTarget.Named, "yapay"), doc), Plan(new RectangleCommand(0, 0), doc),
            Plan(new RotateCommand(45, RotationDirection.Clockwise), doc, Sel(doc)),
        };
        foreach (var result in results)
            foreach (var word in new[] { "yapay zeka", "asistan", "model" })
                Assert.DoesNotContain(word, result.Refusal!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FinishDrawingNeedsADrawingAndEnoughPoints()
    {
        var doc = B1();
        Assert.Equal("R28a", CommandPlanner.Plan(new FinishDrawingCommand(FinishDrawingAction.Close), View(doc), Selection.None).Code);
        var one = new DocumentView(doc, Epoch) { DrawingInProgress = true, DrawingPointCount = 2 };
        Assert.Equal("R28a", CommandPlanner.Plan(new FinishDrawingCommand(FinishDrawingAction.Close), one, Selection.None).Code);
        Assert.True(CommandPlanner.Plan(new FinishDrawingCommand(FinishDrawingAction.FinishOpen), one, Selection.None).IsPlan);
        Assert.True(CommandPlanner.Plan(new FinishDrawingCommand(FinishDrawingAction.Cancel), one, Selection.None).Plan!.Danger);
    }

    [Fact]
    public void NullCommandAndNullViewNeverThrow()
    {
        Assert.False(CommandPlanner.Plan((Command)null!, View(B1()), Selection.None).IsPlan);
        Assert.False(CommandPlanner.Plan(new RectangleCommand(1, 1), null!, Selection.None).IsPlan);
        Assert.False(CommandPlanner.Plan((ParseResult)null!, View(B1()), Selection.None).IsPlan);
    }
}
