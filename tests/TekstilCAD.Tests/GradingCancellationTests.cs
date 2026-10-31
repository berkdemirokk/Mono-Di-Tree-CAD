using System.Collections;
using TekstilCAD.Core;
using TekstilCAD.Grading;

namespace TekstilCAD.Tests;

public sealed class GradingCancellationTests
{
    private sealed class CancelAfterReads<T>(IReadOnlyList<T> items, CancellationTokenSource cancellation, int threshold) : IReadOnlyList<T>
    {
        private int reads;
        public int Count => items.Count;
        public T this[int index]
        {
            get { if (++reads == threshold) cancellation.Cancel(); return items[index]; }
        }
        public IEnumerator<T> GetEnumerator() { for (var i = 0; i < Count; i++) yield return this[i]; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static List<Point2> Circle(int count) => Enumerable.Range(0, count).Select(i =>
    {
        var angle = Math.Tau * i / count;
        return new Point2(1000 * Math.Cos(angle), 1000 * Math.Sin(angle));
    }).ToList();

    [Fact]
    public void IntersectionScanObservesCancellationDuringQuadraticWork()
    {
        var points = Circle(512);
        using var cancellation = new CancellationTokenSource();
        // The validation enumeration consumes 512 reads. Trigger within the first
        // outer edge's intersection comparisons, not before the scan starts.
        var observed = new CancelAfterReads<Point2>(points, cancellation, 1000);
        Assert.Throws<OperationCanceledException>(() => Geometry.HasSelfIntersections(observed, true, cancellation.Token));
        Assert.False(Geometry.HasSelfIntersections(points, true));
    }

    [Fact]
    public void GradingCancellationDuringPointGenerationDoesNotMutateInput()
    {
        var points = Circle(128);
        var piece = new PatternPiece { Name = "Temel", Points = points.ToList(), Closed = true };
        var increments = Enumerable.Repeat(new Point2(1, 2), points.Count).ToArray();
        var before = piece.Clone();
        using var cancellation = new CancellationTokenSource();
        var observed = new CancelAfterReads<Point2>(increments, cancellation, 3);
        Assert.Throws<OperationCanceledException>(() => GradingEngine.Generate(piece, observed, 1, "L", cancellation.Token));
        Assert.Equal(before.Name, piece.Name);
        Assert.Equal(before.Id, piece.Id);
        Assert.Equal(before.Points, piece.Points);
        Assert.All(increments, point => Assert.Equal(new Point2(1, 2), point));
    }

    [Fact]
    public void CancelledBeforeStartRejectsSingleAndBatchWithoutMutation()
    {
        var piece = new PatternPiece { Points = [new(0, 0), new(20, 0), new(20, 20), new(0, 20)] };
        var original = piece.Points.ToArray();
        var increments = Enumerable.Repeat(new Point2(1, 1), original.Length).ToArray();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => GradingEngine.Generate(piece, increments, 1, "L", cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => GradingEngine.GenerateSizes(piece, increments, [new("L", 1)], cancellation.Token));
        Assert.Equal(original, piece.Points);
    }

    [Fact]
    public void UncancelledOverloadMatchesOriginalGeometryAndValidation()
    {
        var piece = new PatternPiece { Name = "Temel", Points = [new(0, 0), new(20, 0), new(20, 20), new(0, 20)] };
        var increments = Enumerable.Repeat(new Point2(1, 1), piece.Points.Count).ToArray();
        var oldResult = GradingEngine.Generate(piece, increments, 1, "L");
        var newResult = GradingEngine.Generate(piece, increments, 1, "L", CancellationToken.None);
        Assert.Equal(oldResult.Name, newResult.Name);
        Assert.Equal(oldResult.Points, newResult.Points);
        Assert.Equal(oldResult.Closed, newResult.Closed);
        Assert.Equal(Geometry.HasSelfIntersections(piece.Points), Geometry.HasSelfIntersections(piece.Points, true, CancellationToken.None));
    }
}
