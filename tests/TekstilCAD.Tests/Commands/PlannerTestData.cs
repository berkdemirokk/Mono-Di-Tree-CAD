using TekstilCAD.Core;
using TekstilCAD.Core.Commands;

namespace TekstilCAD.Tests;

/// <summary>Documents, views and shortcuts shared by the planner tests (contexts B0-B8 of FINAL_COMMANDS.md section 5).</summary>
public static class PlannerTestData
{
    public static readonly Guid Epoch = new("11111111-2222-3333-4444-555555555555");

    public static PatternPiece Piece(string name, double x, double y, double width, double height) =>
        PieceOperationTestData.Rect(name, x, y, width, height);

    /// <summary>B1: three pieces, "Ön beden" 480 x 620 first.</summary>
    public static ProjectDocument B1() => PieceOperationTestData.Doc(
        Piece("Ön beden", 20, 20, 480, 620),
        Piece("Arka beden", 520, 20, 480, 620),
        Piece("Kol", 1020, 20, 200, 300));

    public static DocumentView View(ProjectDocument document, int undo = 3, bool drawing = false, Guid? epoch = null) =>
        new(document, epoch ?? Epoch) { UndoCount = undo, DrawingInProgress = drawing };

    public static Selection Sel(ProjectDocument document, int index = 0) => Selection.Of(document.Pieces[index].Id);

    public static PlanResult Plan(Command command, ProjectDocument document, Selection? selection = null, bool unitAssumed = false, DocumentView? view = null) =>
        CommandPlanner.Plan(command, view ?? View(document), selection ?? Selection.None, unitAssumed);

    /// <summary>The plan, failing the test with the refusal text when there is none.</summary>
    public static Plan Ok(PlanResult result)
    {
        Assert.True(result.IsPlan, result.Refusal?.Code + ": " + result.Refusal?.Message);
        return result.Plan!;
    }

    public static Refusal Refused(PlanResult result)
    {
        Assert.False(result.IsPlan, result.Plan?.Preview);
        return result.Refusal!;
    }
}
