using TekstilCAD.Core;
using TekstilCAD.Core.Commands;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DartCommandTests
{
    private static ProjectDocument Rectangle() => new()
    {
        Pieces = [new PatternPiece { Name = "Ön beden", Points = [new(0,0),new(100,0),new(100,100),new(0,100)] }]
    };

    [Theory]
    [InlineData("2 kenara 40 mm ağızlı 20 mm derinlikte pens ekle", 40, 20)]
    [InlineData("2 kenara 4 cm ağızlı 2 cm derinlikte pens ekle", 40, 20)]
    public void TurkishExampleParsesExplicitMillimeterMeasures(string text, double intake, double depth)
    {
        var parsed=CommandParser.Parse(text);
        Assert.True(parsed.IsCommand,parsed.ToString());
        Assert.Equal(new DartCommand(2,intake,depth),parsed.Command);
        Assert.False(parsed.UnitAssumed);
    }

    [Theory]
    [InlineData("pens ekle")]
    public void MissingOrImpossibleDartInputsAreRefused(string text)
        => Assert.False(CommandParser.Parse(text).IsCommand);

    [Theory]
    [InlineData("9 kenara 40 mm ağızlı 20 mm derinlikte pens ekle")]
    [InlineData("2 kenara 200 mm ağızlı 20 mm derinlikte pens ekle")]
    public void PlannerRefusesOutOfRangeEdgeOrMouth(string text)
    {
        var doc=Rectangle();var piece=doc.Pieces.Single();
        var parsed=CommandParser.Parse(text);
        Assert.True(parsed.IsCommand,parsed.ToString());
        Assert.False(CommandPlanner.Plan(parsed,new DocumentView(doc,Guid.NewGuid()),Selection.Of(piece.Id)).IsPlan);
    }

    [Fact]
    public void CommandPreviewShowsSewingLegsAndApplyUndoRedoPreserveOutline()
    {
        var session=new DocumentSession(Rectangle());
        var piece=session.Document.Pieces.Single();
        var parsed=CommandParser.Parse("1 kenara 40 mm ağızlı 20 mm derinlikte pens ekle");
        var plan=CommandPlanner.Plan(parsed,new DocumentView(session.Document,Guid.NewGuid()),Selection.Of(piece.Id)).Plan!;
        Assert.Equal(PlanKind.AddDart,plan.Kind);
        Assert.Equal(2,plan.Ghost.Lines.Count);
        Assert.Contains("Kontur değişmez",plan.Preview);
        Assert.True(session.Execute(plan.Apply,ProjectStorage.Validate));
        Assert.Equal(new[]{new Point2(0,0),new Point2(100,0),new Point2(100,100),new Point2(0,100)},session.Document.Pieces.Single().Points);
        Assert.Single(session.Document.Pieces.Single().Darts!);
        Assert.True(session.Undo());
        Assert.Null(session.Document.Pieces.Single().Darts);
        Assert.True(session.Redo());
        Assert.Single(session.Document.Pieces.Single().Darts!);
    }
}
