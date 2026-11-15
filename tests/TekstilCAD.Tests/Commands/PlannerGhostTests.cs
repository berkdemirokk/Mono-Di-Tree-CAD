using TekstilCAD.Core;
using TekstilCAD.Core.Commands;
using static TekstilCAD.Tests.PlannerTestData;

namespace TekstilCAD.Tests;

/// <summary>The dashed frame of each plan, with exact millimetre coordinates.</summary>
public class PlannerGhostTests
{
    private static GhostPolygon Single(Plan plan) => Assert.Single(plan.Ghost.Polygons);

    [Fact]
    public void RectangleFrameAndMeasureLabels()
    {
        var ghost = Ok(Plan(new RectangleCommand(120, 80), new ProjectDocument())).Ghost;
        Assert.Equal(GhostStyle.Preview, ghost.Style);
        var polygon = Assert.Single(ghost.Polygons);
        Assert.True(polygon.Closed);
        Assert.Equal([new Point2(20, 20), new Point2(140, 20), new Point2(140, 100), new Point2(20, 100)], polygon.Points);
        Assert.Equal([new GhostLabel(new Point2(80, 20), "120 mm", GhostLabelPlacement.Above),
                      new GhostLabel(new Point2(140, 60), "80 mm", GhostLabelPlacement.Right)], ghost.Labels);
        Assert.Equal(new Bounds2(20, 20, 140, 100), ghost.Bounds);
    }

    [Fact]
    public void LineFramesAreOpen()
    {
        var horizontal = Ok(Plan(new LineCommand(150, LineOrientation.Horizontal), new ProjectDocument())).Ghost;
        Assert.False(horizontal.Polygons[0].Closed);
        Assert.Equal([new Point2(20, 20), new Point2(170, 20)], horizontal.Polygons[0].Points);
        Assert.Equal(new GhostLabel(new Point2(95, 20), "150 mm", GhostLabelPlacement.Above), horizontal.Labels.Single());
        var vertical = Ok(Plan(new LineCommand(80, LineOrientation.Vertical), new ProjectDocument())).Ghost;
        Assert.Equal([new Point2(20, 20), new Point2(20, 100)], vertical.Polygons[0].Points);
        Assert.Equal(new GhostLabel(new Point2(20, 60), "80 mm", GhostLabelPlacement.Right), vertical.Labels.Single());
    }

    [Fact]
    public void MoveFrameIsTheMovedOutline()
    {
        var doc = B1();
        var ghost = Ok(Plan(new MoveByCommand(25, 0), doc, Sel(doc))).Ghost;
        Assert.Equal([new Point2(45, 20), new Point2(525, 20), new Point2(525, 640), new Point2(45, 640)], ghost.Polygons.Single().Points);
        Assert.Equal(new GhostLabel(new Point2(285, 20), "25 mm sağa", GhostLabelPlacement.Above), ghost.Labels.Single());
    }

    [Fact]
    public void RotateFrameHasTheSwappedBoundsAndAnAngleLabel()
    {
        var doc = B1();
        var ghost = Ok(Plan(new RotateCommand(90, RotationDirection.Clockwise), doc, Sel(doc))).Ghost;
        var bounds = ghost.Bounds!.Value;
        Assert.Equal(-50, bounds.MinX, 6);
        Assert.Equal(570, bounds.MaxX, 6);
        Assert.Equal(90, bounds.MinY, 6);
        Assert.Equal(570, bounds.MaxY, 6);
        Assert.Equal("90° saat yönünde", ghost.Labels.Single().Text);
    }

    [Fact]
    public void MirrorFrameFlipsLeftAndRight()
    {
        var doc = B1();
        var ghost = Ok(Plan(new MirrorCommand(MirrorAxis.Vertical), doc, Sel(doc))).Ghost;
        Assert.Equal(new Bounds2(20, 20, 500, 640), ghost.Bounds);
        Assert.Equal(new Point2(500, 20), ghost.Polygons.Single().Points[0]);
        Assert.Equal(new Point2(20, 20), ghost.Polygons.Single().Points[1]);
    }

    [Fact]
    public void ResizeFrameKeepsTheTopLeftCorner()
    {
        var doc = PieceOperationTestData.Doc(Piece("Parça 1", 20, 20, 120, 80));
        var ghost = Ok(Plan(new ResizeCommand(95, null), doc, Sel(doc))).Ghost;
        Assert.Equal([new Point2(20, 20), new Point2(115, 20), new Point2(115, 100), new Point2(20, 100)], ghost.Polygons.Single().Points);
        Assert.Equal(["95 mm", "80 mm"], ghost.Labels.Select(l => l.Text));
    }

    [Fact]
    public void DeleteFrameIsRedAndLabelledSilinecek()
    {
        var doc = B1();
        var ghost = Ok(Plan(new DeleteCommand(PieceTarget.Selected), doc, Sel(doc))).Ghost;
        Assert.Equal(GhostStyle.Danger, ghost.Style);
        Assert.Equal(doc.Pieces[0].Points, ghost.Polygons.Single().Points);
        Assert.Equal(new GhostLabel(new Point2(260, 330), "silinecek", GhostLabelPlacement.Center), ghost.Labels.Single());
    }

    [Fact]
    public void FramesAreEmptyWhereNothingCanBeDrawn()
    {
        var doc = B1();
        Assert.True(Ok(Plan(new RenameCommand("X"), doc, Sel(doc))).Ghost.IsEmpty);
        Assert.True(Ok(Plan(new ViewCommand(ViewAction.FitAll), doc)).Ghost.IsEmpty);
        Assert.Null(GhostScene.Empty.Bounds);
    }

    [Fact]
    public void FramesNeverShareStateWithTheDocument()
    {
        var doc = B1();
        var plan = Ok(Plan(new DeleteCommand(PieceTarget.Selected), doc, Sel(doc)));
        doc.Pieces[0].Points[0] = new Point2(-1, -1);
        Assert.Equal(new Point2(20, 20), Single(plan).Points[0]);
    }
}
