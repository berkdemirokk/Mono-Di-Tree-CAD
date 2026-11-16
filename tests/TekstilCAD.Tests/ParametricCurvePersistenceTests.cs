using System.Text.Json.Nodes;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class ParametricCurvePersistenceTests
{
    private static CurveSegment.CubicBezier Bezier(double height = 30) =>
        new(new(0, 0), new(0, height), new(100, height), new(100, 0));

    private static ProjectDocument Sample(CurveSegment curve)
    {
        var segments = new List<CurveSegment> { curve };
        return new ProjectDocument
        {
            Name = "Parametrik çizim",
            Pieces = [new PatternPiece
            {
                Name = "Kontrol noktaları",
                Closed = false,
                Curves = segments,
                Points = ParametricContour.Sample(segments)
            }]
        };
    }

    private static string FilePath() =>
        Path.Combine(Path.GetTempPath(), "MonoDiTreeCurveTests-" + Guid.NewGuid().ToString("N") + ".tcad");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VersionNineRoundTripPreservesEditableControls(bool useArc)
    {
        var curve = useArc
            ? (CurveSegment)new CurveSegment.Arc(new(10, 20), 30, 0, Math.PI / 2)
            : Bezier();
        var source = Sample(curve);
        var path = FilePath();
        try
        {
            ProjectStorage.Save(source, path);
            var text = File.ReadAllText(path);
            Assert.Equal(9, JsonNode.Parse(text)!.AsArray()[0]!.GetValue<int>());
            Assert.Contains("\"$type\"", text);
            var loaded = ProjectStorage.Load(path);
            Assert.True(DocumentSession.ContentEquals(source, loaded));
            var piece = Assert.Single(loaded.Pieces);
            Assert.Equal(source.Pieces[0].Points, piece.Points);
            var restored = Assert.Single(piece.Curves!);
            Assert.Equal(curve, restored);
            Assert.True(ParametricContour.MatchesPreview(piece));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ControlEditsAreUndoableAndInvalidateTheSavedCheckpoint()
    {
        var session = new DocumentSession(Sample(Bezier()));
        session.MarkSaved();
        Assert.True(session.Execute(d =>
        {
            var piece = d.Pieces[0];
            piece.Curves = [Bezier(50)];
            ParametricContour.Regenerate(piece);
        }, ProjectStorage.Validate));
        Assert.True(session.IsDirty);
        Assert.Equal(1, session.UndoCount);
        Assert.True(session.Undo());
        Assert.False(session.IsDirty);
        Assert.Equal(Bezier(), Assert.Single(session.Document.Pieces[0].Curves!));
        Assert.True(session.Redo());
        Assert.True(session.IsDirty);
        Assert.Equal(Bezier(50), Assert.Single(session.Document.Pieces[0].Curves!));
    }

    [Fact]
    public void StaleSampleCannotSilentlySaveOverExistingFile()
    {
        var path = FilePath();
        try
        {
            var doc = Sample(Bezier());
            ProjectStorage.Save(doc, path);
            var before = File.ReadAllBytes(path);
            doc.Pieces[0].Points[1] = new(999, 999);
            Assert.Throws<InvalidDataException>(() => ProjectStorage.Save(doc, path));
            Assert.Equal(before, File.ReadAllBytes(path));
            // A deliberate detach makes the original sampled polyline editable.
            ParametricContour.Detach(doc.Pieces[0]);
            ProjectStorage.Validate(doc);
            ProjectStorage.Save(doc, path);
            Assert.Null(ProjectStorage.Load(path).Pieces[0].Curves);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("old-version")]
    [InlineData("future-type")]
    public void MalformedCurveEnvelopeFailsClosedWithoutMutatingFile(string mutation)
    {
        var path = FilePath();
        try
        {
            ProjectStorage.Save(Sample(Bezier()), path);
            var root = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
            var obj = root[1]!.AsObject();
            var curve = obj["Pieces"]![0]!["Curves"]![0]!.AsObject();
            switch (mutation)
            {
                case "missing": curve.Remove("P1"); break;
                case "unknown": curve["UnknownMetadata"] = 27; break;
                case "old-version": root[0] = 8; break;
                case "future-type": curve["$type"] = "quadratic"; break;
            }
            var corrupted = root.ToJsonString();
            File.WriteAllText(path, corrupted);
            Assert.Throws<InvalidDataException>(() => ProjectStorage.Load(path));
            Assert.Equal(corrupted, File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    // The desktop arc tool turns its three clicks into centre, radius and
    // angles with this arithmetic. Polar parameters cannot reproduce the
    // clicked Cartesian points bit for bit, so the end points are held to a
    // tolerance; only the points sampled from the stored arc are exact.
    [Theory]
    [InlineData(0, 0, 1, 1, -1, 1)]
    [InlineData(350.5, 720.25, 458.5, 864.25, 206.5, 828.25)]
    [InlineData(-5000, 5000, -5000, 0, 0, 5000)]
    public void ArcFromClickedPointsStaysWithinToleranceAndSurvivesUndoRedo(
        double cx, double cy, double sx, double sy, double ex, double ey)
    {
        Point2 center = new(cx, cy), start = new(sx, sy), end = new(ex, ey);
        var a = Math.Atan2(start.Y - center.Y, start.X - center.X);
        var b = Math.Atan2(end.Y - center.Y, end.X - center.X);
        var arc = new CurveSegment.Arc(center, Geometry.Distance(center, start), a, (b - a + 2 * Math.PI) % (2 * Math.PI));
        var drawn = Sample(arc);
        var piece = drawn.Pieces[0];
        Assert.InRange(Geometry.Distance(piece.Points[0], start), 0, 1e-9);
        Assert.InRange(Geometry.Distance(piece.Points[^1], end), 0, 1e-9);
        Assert.True(ParametricContour.MatchesPreview(piece));

        var session = new DocumentSession(new ProjectDocument { Name = drawn.Name });
        Assert.True(session.Execute(d => d.Pieces.Add(piece.Clone()), ProjectStorage.Validate));
        Assert.True(session.Undo());
        Assert.Empty(session.Document.Pieces);
        Assert.True(session.Redo());
        Assert.True(DocumentSession.ContentEquals(drawn, session.Document));
    }

    [Fact]
    public void LegacyPolylineRemainsInItsOriginalUnversionedFormat()
    {
        var project = new ProjectDocument { Pieces = [new PatternPiece
        {
            Closed = false, Points = [new(0, 0), new(100, 100)]
        }] };
        var path = FilePath();
        try
        {
            ProjectStorage.Save(project, path);
            Assert.StartsWith("{", File.ReadAllText(path));
            Assert.True(DocumentSession.ContentEquals(project, ProjectStorage.Load(path)));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NoncontinuousAndClosedCurvePathsFailBeforeSave()
    {
        var doc = Sample(Bezier());
        doc.Pieces[0].Curves!.Add(new CurveSegment.Line(new(200, 200), new(300, 200)));
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Validate(doc));
        doc = Sample(Bezier());
        doc.Pieces[0].Closed = true;
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Validate(doc));
    }
}
