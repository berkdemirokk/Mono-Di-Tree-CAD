using System.Diagnostics;
using TekstilCAD.Core;
using TekstilCAD.Core.Commands;

namespace TekstilCAD.Tests;

/// <summary>
/// Planning must stay fast and roughly linear in the number of pieces. The bounds are generous on purpose (a loaded CI machine
/// must not make these flaky); they exist to catch an accidental quadratic or cubic loop, not to benchmark. Each measurement
/// is the best of several passes after a warm-up, so one garbage collection or one busy moment does not count.
/// </summary>
public sealed class PlannerPerformanceTests
{
    private const int Repetitions = 5;

    /// <summary>Pieces in a grid of 50 columns, each 80 x 60 mm with a 20 mm gap, named "Parça 1", "Parça 2", ...</summary>
    private static ProjectDocument Build(int count)
    {
        var pieces = new PatternPiece[count];
        for (var i = 0; i < count; i++)
            pieces[i] = PieceOperationTestData.Rect("Parça " + (i + 1), (i % 50) * 100, (i / 50) * 100, 80, 60);
        return PieceOperationTestData.Doc(pieces);
    }

    private sealed record Scenario(string Name, Command Command);

    private static IReadOnlyList<Scenario> Scenarios(ProjectDocument document)
    {
        var middle = document.Pieces.Count / 2;
        return
        [
            new Scenario("rectangle", new RectangleCommand(120, 80)),
            new Scenario("move", new MoveByCommand(25, 10)),
            new Scenario("rotate", new RotateCommand(90, RotationDirection.Clockwise)),
            new Scenario("delete", new DeleteCommand(PieceTarget.Selected)),
            new Scenario("select next", new SelectCommand(PieceTarget.Next)),
            new Scenario("select named", new SelectCommand(PieceTarget.Named, "parça " + (middle + 1))),
        ];
    }

    private static (DocumentView View, Selection Selection) Context(ProjectDocument document) =>
        (new DocumentView(document, PlannerHostileTests.Epoch) { UndoCount = 3, RedoCount = 1 },
         Selection.Of(document.Pieces[document.Pieces.Count / 2].Id));

    /// <summary>Plans every scenario once and returns the milliseconds all of them took together; every one must give a plan.</summary>
    private static double PassAll(ProjectDocument document)
    {
        var (view, selection) = Context(document);
        var scenarios = Scenarios(document);
        var results = new PlanResult[scenarios.Count];
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < scenarios.Count; i++) results[i] = CommandPlanner.Plan(scenarios[i].Command, view, selection);
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        for (var i = 0; i < results.Length; i++) Assert.True(results[i].IsPlan, $"{scenarios[i].Name}: {results[i]}");
        return elapsed;
    }

    private static double BestOfAll(ProjectDocument document)
    {
        var best = double.MaxValue;
        for (var i = 0; i < Repetitions; i++) best = Math.Min(best, PassAll(document));
        return best;
    }

    private static double BestOf(ProjectDocument document, Scenario scenario)
    {
        var (view, selection) = Context(document);
        var best = double.MaxValue;
        for (var i = 0; i < Repetitions; i++)
        {
            var start = Stopwatch.GetTimestamp();
            var result = CommandPlanner.Plan(scenario.Command, view, selection);
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Assert.True(result.IsPlan, $"{scenario.Name}: {result}");
            best = Math.Min(best, elapsed);
        }
        return best;
    }

    /// <summary>Runs everything once on a small document so that JIT compilation is not measured.</summary>
    private static void WarmUp()
    {
        var small = Build(30);
        PassAll(small);
        PassAll(small);
    }

    [Fact]
    public void BuiltDocumentsHaveTheRequestedNumberOfDistinctPieces()
    {
        foreach (var count in new[] { 30, 250, 2000 })
        {
            var document = Build(count);
            Assert.Equal(count, document.Pieces.Count);
            Assert.Equal(count, document.Pieces.Select(p => p.Id).Distinct().Count());
            Assert.Equal(count, document.Pieces.Select(p => p.Name).Distinct().Count());
        }
    }

    [Fact]
    public void AllSixPlansOnTwoHundredFiftyPiecesTogetherTakeUnderTwoSeconds()
    {
        WarmUp();
        var milliseconds = BestOfAll(Build(250));
        Assert.True(milliseconds < 2000, $"rectangle, move, rotate, delete, select next and select named took {milliseconds:F1} ms together on 250 pieces");
    }

    [Fact]
    public void EachPlanOnTwoThousandPiecesTakesUnderOneAndAHalfSeconds()
    {
        WarmUp();
        var document = Build(2000);
        foreach (var scenario in Scenarios(document))
        {
            var milliseconds = BestOf(document, scenario);
            Assert.True(milliseconds < 1500, $"{scenario.Name} took {milliseconds:F1} ms on 2000 pieces");
        }
    }

    [Fact]
    public void PlanningTimeGrowsRoughlyLinearlyWithTheNumberOfPieces()
    {
        WarmUp();
        var small = BestOfAll(Build(250));
        var large = BestOfAll(Build(2000));
        // Eight times the pieces may cost eight times the time; a bound of 25 times plus half a second only trips on a quadratic loop.
        Assert.True(large < 25 * small + 500,
            $"250 pieces took {small:F2} ms, 2000 pieces took {large:F2} ms (more than 25 times plus 500 ms)");
    }

    [Fact]
    public void EachPlanGrowsRoughlyLinearlyWithTheNumberOfPieces()
    {
        WarmUp();
        var smallDocument = Build(250);
        var largeDocument = Build(2000);
        var smallScenarios = Scenarios(smallDocument);
        var largeScenarios = Scenarios(largeDocument);
        for (var i = 0; i < smallScenarios.Count; i++)
        {
            var small = BestOf(smallDocument, smallScenarios[i]);
            var large = BestOf(largeDocument, largeScenarios[i]);
            Assert.True(large < 25 * small + 500,
                $"{smallScenarios[i].Name}: 250 pieces took {small:F2} ms, 2000 pieces took {large:F2} ms");
        }
    }

    [Fact]
    public void PlanningOnLargeDocumentsLeavesTheDocumentAndSelectionUntouched()
    {
        foreach (var count in new[] { 250, 2000 })
        {
            var document = Build(count);
            var snapshot = document.Clone();
            var (view, selection) = Context(document);
            var before = selection.PieceIds.ToArray();
            foreach (var scenario in Scenarios(document)) Assert.True(CommandPlanner.Plan(scenario.Command, view, selection).IsPlan, scenario.Name);
            Assert.True(DocumentSession.ContentEquals(snapshot, document), $"planning changed the document of {count} pieces");
            Assert.True(before.SequenceEqual(selection.PieceIds.ToArray()));
        }
    }

    [Fact]
    public void PlansOnLargeDocumentsStayCorrectAndDeterministic()
    {
        var document = Build(2000);
        var (view, selection) = Context(document);
        var middle = document.Pieces[1000];

        var delete = CommandPlanner.Plan(new DeleteCommand(PieceTarget.Selected), view, selection);
        Assert.True(delete.IsPlan, delete.ToString());
        Assert.Equal("‘Parça 1001’ silinecek (80 × 60 mm). Geri alabilirsiniz.", delete.Plan!.Preview);
        Assert.Equal(1999, delete.Plan.EditedDocument()!.Pieces.Count);

        var next = CommandPlanner.Plan(new SelectCommand(PieceTarget.Next), view, selection);
        Assert.Equal(document.Pieces[1001].Id, next.Plan!.TargetPieceId);

        var named = CommandPlanner.Plan(new SelectCommand(PieceTarget.Named, "PARÇA 1001"), view, selection);
        Assert.True(named.IsPlan, named.ToString());
        Assert.Equal(middle.Id, named.Plan!.TargetPieceId);

        var firstRectangle = CommandPlanner.Plan(new RectangleCommand(120, 80), view, selection);
        var secondRectangle = CommandPlanner.Plan(new RectangleCommand(120, 80), view, selection);
        Assert.True(firstRectangle.IsPlan, firstRectangle.ToString());
        Assert.Equal(firstRectangle.Plan!.Preview, secondRectangle.Plan!.Preview);
        Assert.Equal(firstRectangle.Plan.Stamp.ContentDigest, secondRectangle.Plan.Stamp.ContentDigest);
        Assert.Equal(2001, firstRectangle.Plan.EditedDocument()!.Pieces.Count);
    }
}
