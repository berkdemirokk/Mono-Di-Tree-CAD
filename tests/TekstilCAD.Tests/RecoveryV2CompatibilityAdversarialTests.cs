using System.Text.Json.Nodes;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class RecoveryV2CompatibilityAdversarialTests
{
    private static string DirectoryForTest()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        return Path.Combine(root.FullName, "work", "recovery-v2-adversarial", Guid.NewGuid().ToString("N"));
    }

    private static ProjectDocument Document() => new()
    {
        Name = "Kalıp",
        Pieces = [new PatternPiece { Name = "Ön", Points = [new(0, 0), new(10, 0), new(0, 10)] }]
    };

    [Theory]
    [InlineData("missing-pending")]
    [InlineData("unknown-pending")]
    [InlineData("unknown-point")]
    [InlineData("missing-x")]
    [InlineData("null-point")]
    [InlineData("string-coordinate")]
    [InlineData("unknown-version")]
    [InlineData("v1-with-pending")]
    [InlineData("duplicate-coordinate")]
    public void InvalidV2DataIsIsolatedAndSourceBytesRemainUnchanged(string corruption)
    {
        var directory = DirectoryForTest();
        string path;
        using (var writer = new RecoveryStore(directory))
        {
            writer.Save(Document(), [], new RecoveryPendingDrawing("Çizgi", [new(1.25, -3.5)]));
            path = writer.DraftRecoveryPath;
        }
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var pending = root["PendingDrawing"]!.AsObject();
        var points = pending["Points"]!.AsArray();
        var point = points[0]!.AsObject();
        switch (corruption)
        {
            case "missing-pending": root.Remove("PendingDrawing"); break;
            case "unknown-pending": pending["Unexpected"] = true; break;
            case "unknown-point": point["Unexpected"] = true; break;
            case "missing-x": point.Remove("X"); break;
            case "null-point": points[0] = null; break;
            case "string-coordinate": point["X"] = "1.25"; break;
            case "unknown-version": root["Version"] = 3; break;
            case "v1-with-pending": root["Version"] = 1; break;
        }
        var text = root.ToJsonString();
        if (corruption == "duplicate-coordinate")
        {
            var marker = "\"X\":1.25";
            Assert.Contains(marker, text);
            text = text.Replace(marker, "\"X\":2," + marker, StringComparison.Ordinal);
        }
        File.WriteAllText(path, text);
        var bytes = File.ReadAllBytes(path);
        using var reader = new RecoveryStore(directory);
        var scan = reader.Scan();
        var item = Assert.Single(scan.Candidates, candidate => candidate.Path == path);
        Assert.False(item.IsRecoverable);
        Assert.Throws<InvalidDataException>(() => reader.LoadCandidate(path));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void V2ThenV1AndBackRetainsTheCorrectEnvelopeSemantics()
    {
        var directory = DirectoryForTest();
        var document = Document();
        string path;
        using (var writer = new RecoveryStore(directory))
        {
            writer.Save(document, [], new RecoveryPendingDrawing("Yay", [new(1, 2), new(3, 4)]));
            writer.Save(document, []);
            writer.Save(document, [], new RecoveryPendingDrawing("Bézier", [new(1, 2), new(3, 4), new(5, 6)]));
            path = writer.DraftRecoveryPath;
        }
        using var reader = new RecoveryStore(directory);
        var snapshot = reader.LoadCandidate(path);
        Assert.True(DocumentSession.ContentEquals(document, snapshot.Document));
        Assert.NotNull(snapshot.PendingDrawing);
        Assert.Equal("Bézier", snapshot.PendingDrawing.Tool);
        Assert.Equal(new[] { new Point2(1, 2), new Point2(3, 4), new Point2(5, 6) }, snapshot.PendingDrawing.Points);
    }

    [Fact]
    public void PendingCoordinatesRoundTripExactlyWithoutAddingProjectGeometry()
    {
        var directory = DirectoryForTest();
        var document = Document();
        var before = document.Clone();
        Point2[] points = [new(-0.125, 0.000000125), new(99999999.125, -99999999.875)];
        string path;
        using (var writer = new RecoveryStore(directory))
        {
            writer.Save(document, [], new RecoveryPendingDrawing("Yay", points));
            path = writer.DraftRecoveryPath;
        }
        using var reader = new RecoveryStore(directory);
        var snapshot = reader.LoadCandidate(path);
        Assert.True(DocumentSession.ContentEquals(before, snapshot.Document));
        Assert.True(DocumentSession.ContentEquals(before, document));
        Assert.NotNull(snapshot.PendingDrawing);
        Assert.Equal(points, snapshot.PendingDrawing.Points);
    }
}
