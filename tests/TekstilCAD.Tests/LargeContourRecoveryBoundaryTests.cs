using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class LargeContourRecoveryBoundaryTests
{
    private static string DirectoryForTest()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        return Path.Combine(root.FullName, "work", "large-contour-recovery-tests", Guid.NewGuid().ToString("N"));
    }

    private static List<Point2> Ring(int count) => Enumerable.Range(0, count).Select(i =>
    {
        var a = Math.Tau * i / count;
        return new Point2(1000 * Math.Cos(a), 1000 * Math.Sin(a));
    }).ToList();

    [Theory]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(10000)]
    public void RecoveryRoundTripsEveryUnfinishedPointWithoutCreatingPiece(int count)
    {
        var directory = DirectoryForTest();
        var points = Ring(count);
        var original = points.ToArray();
        var document = new ProjectDocument { Name = "Büyük çizim" };
        string path;
        using (var writer = new RecoveryStore(directory))
        {
            writer.Save(document, [], new RecoveryPendingDrawing("Çoklu çizgi", points));
            path = writer.DraftRecoveryPath;
        }
        using var reader = new RecoveryStore(directory);
        var restored = reader.LoadCandidate(path);
        Assert.Empty(restored.Document.Pieces);
        Assert.NotNull(restored.PendingDrawing);
        Assert.Equal(original, restored.PendingDrawing.Points);
        Assert.Equal(original, points);
    }

    [Fact]
    public void Exactly4096DistinctVerticesIncludingOptionalClosureAreAccepted()
    {
        var points = Ring(4096);
        Geometry.ValidateContour(points);
        points.Add(points[0]);
        Geometry.ValidateContour(points);
    }

    [Theory]
    [InlineData(4097)]
    [InlineData(10000)]
    public void CurrentContourLimitRejectsOversizedRingWithoutMutatingInput(int count)
    {
        var points = Ring(count);
        var original = points.ToArray();
        Assert.Throws<ArgumentException>(() => Geometry.ValidateContour(points));
        Assert.Equal(original, points);
    }

    [Fact]
    public void RepeatedInteriorPointCannotBeTreatedAsOptionalClosure()
    {
        var points = Ring(256);
        points[128] = points[3];
        var original = points.ToArray();
        Assert.Throws<ArgumentException>(() => Geometry.ValidateContour(points));
        Assert.Equal(original, points);
    }

    [Fact]
    public void CrossingAtClosingEdgeIsRejectedWithoutChangingContour()
    {
        // The last-to-first edge crosses a nonadjacent interior edge.
        List<Point2> points = [new(0, 0), new(4, 0), new(4, 4), new(0, 4), new(2, -1)];
        var original = points.ToArray();
        Assert.Throws<ArgumentException>(() => Geometry.ValidateContour(points));
        Assert.Equal(original, points);
    }
}
