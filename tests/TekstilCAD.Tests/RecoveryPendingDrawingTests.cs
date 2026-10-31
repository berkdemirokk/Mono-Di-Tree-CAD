using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class RecoveryPendingDrawingTests
{
    private static string DirectoryForTest()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        return Path.Combine(root.FullName, "work", "recovery-pending-tests", Guid.NewGuid().ToString("N"));
    }
    private static ProjectDocument Document() => new() { Name = "Örnek", Pieces = [new PatternPiece { Name = "Ön", Points = [new(0,0),new(20,0),new(0,20)] }] };
    private static RecoveryPropertyDraft Draft(ProjectDocument doc) => new(doc.Pieces[0].Id,"Taslak","?","0","Ön",0,0);

    [Theory]
    [InlineData("Çizgi", 1)]
    [InlineData("Dikdörtgen", 1)]
    [InlineData("Bézier", 3)]
    [InlineData("Yay", 2)]
    [InlineData("Çoklu çizgi", 10000)]
    public void SnapshotRoundTripsUnfinishedPointsWithoutApplyingGeometry(string tool, int count)
    {
        var dir = DirectoryForTest(); var doc = Document(); var original = doc.Clone();
        var points = Enumerable.Range(0,count).Select(i => new Point2(i, i * 0.125)).ToArray();
        var draft = Draft(doc); string path;
        using (var writer = new RecoveryStore(dir))
        { writer.Save(doc, [draft], new RecoveryPendingDrawing(tool, points)); path = writer.DraftRecoveryPath; }
        using var reader = new RecoveryStore(dir);
        var recovered = reader.LoadCandidate(path);
        Assert.True(DocumentSession.ContentEquals(original, recovered.Document));
        Assert.Equal(draft, Assert.Single(recovered.Drafts));
        Assert.NotNull(recovered.PendingDrawing);
        Assert.Equal(tool, recovered.PendingDrawing.Tool);
        Assert.Equal(points, recovered.PendingDrawing.Points);
        Assert.True(DocumentSession.ContentEquals(original, doc));
    }

    [Fact]
    public void VersionOneEnvelopeRemainsReadableWithoutPendingDrawing()
    {
        var dir = DirectoryForTest(); var doc = Document(); string path;
        using (var writer = new RecoveryStore(dir)) { writer.Save(doc, [Draft(doc)]); path = writer.DraftRecoveryPath; }
        using var reader = new RecoveryStore(dir);
        var restored = reader.LoadCandidate(path);
        Assert.Null(restored.PendingDrawing);
        Assert.True(DocumentSession.ContentEquals(doc, restored.Document));
        Assert.Single(restored.Drafts);
    }

    [Fact]
    public void SnapshotWithoutPendingDrawingRoundTripsAsAbsent()
    {
        var dir = DirectoryForTest(); string path; var doc = Document();
        using (var writer = new RecoveryStore(dir)) { writer.Save(doc, [], null); path = writer.DraftRecoveryPath; }
        using var reader = new RecoveryStore(dir);
        Assert.Null(reader.LoadCandidate(path).PendingDrawing);
    }

    [Theory]
    [InlineData("Seç", 1)]
    [InlineData("", 1)]
    [InlineData("Çizgi", 0)]
    [InlineData("Çizgi", 2)]
    [InlineData("Dikdörtgen", 2)]
    [InlineData("Bézier", 4)]
    [InlineData("Yay", 3)]
    [InlineData("Çoklu çizgi", 10001)]
    public void InvalidToolOrCountDoesNotReplaceLastCompleteSnapshot(string tool, int count)
    {
        var dir = DirectoryForTest(); var doc = Document();
        using var writer = new RecoveryStore(dir);
        writer.Save(doc, [], new RecoveryPendingDrawing("Çizgi", [new(2,3)]));
        var path = writer.DraftRecoveryPath; var original = File.ReadAllBytes(path);
        var invalid = new RecoveryPendingDrawing(tool, Enumerable.Range(0,count).Select(i=>new Point2(i,0)).ToArray());
        Assert.Throws<InvalidDataException>(() => writer.Save(doc, [], invalid));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(0, double.NegativeInfinity)]
    [InlineData(1000000001, 0)]
    public void InvalidCoordinatesDoNotReplaceLastCompleteSnapshot(double x, double y)
    {
        var dir = DirectoryForTest(); var doc = Document();
        using var writer = new RecoveryStore(dir);
        writer.Save(doc, [], new RecoveryPendingDrawing("Çizgi", [new(2,3)]));
        var path = writer.DraftRecoveryPath; var original = File.ReadAllBytes(path);
        Assert.Throws<InvalidDataException>(() => writer.Save(doc, [], new RecoveryPendingDrawing("Çizgi", [new(x,y)])));
        Assert.Equal(original, File.ReadAllBytes(path));
    }
}

