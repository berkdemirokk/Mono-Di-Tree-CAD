using System.Collections;
using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public sealed class LargeValidatorIntegratedStressTests
{
    private static List<Point2> Radial(int count, double origin, bool alternating = false) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var angle = Math.Tau * i / count;
            var radius = alternating && i % 2 != 0 ? 10.0 : 1000.0;
            return new Point2(origin + radius * Math.Cos(angle), origin + radius * Math.Sin(angle));
        }).ToList();

    [Theory]
    [InlineData(4097, 0.0, false)]
    [InlineData(5000, 50000000.0, true)]
    public void LargeSimpleContoursIncludingOverlappingBoxesAreAcceptedWithoutMutation(int count, double origin, bool alternating)
    {
        var points = Radial(count, origin, alternating);
        var before = points.ToArray();
        LargeContourValidator.Validate(points);
        Assert.Equal(before, points);
    }

    [Fact]
    public void CrossingNearStartOfLargeContourIsRejectedWithoutMutation()
    {
        var points = Radial(4097, 0);
        // Swapping separated vertices introduces crossings while retaining a
        // nonzero-area, distinct-vertex input for the intersection check.
        (points[1], points[2048]) = (points[2048], points[1]);
        var before = points.ToArray();
        Assert.True(Math.Abs(Geometry.SignedArea(points)) > Geometry.Epsilon);
        Assert.Equal(points.Count, points.Distinct().Count());
        Assert.Throws<ArgumentException>(() => LargeContourValidator.Validate(points));
        Assert.Equal(before, points);
    }

    [Fact]
    public void CancellationDuringSnapshotCopyStopsAndLeavesOriginalUnchanged()
    {
        var points = Radial(4097, 0);
        var before = points.ToArray();
        using var cancellation = new CancellationTokenSource();
        var source = new CancelDuringRead(points, cancellation, 1500);
        Assert.ThrowsAny<OperationCanceledException>(() => LargeContourValidator.Validate(source, cancellation.Token));
        Assert.InRange(source.Reads, 1501, 2049);
        Assert.Equal(before, points);
    }

    private sealed class CancelDuringRead(IReadOnlyList<Point2> source, CancellationTokenSource cancellation, int index) : IReadOnlyList<Point2>
    {
        public int Reads { get; private set; }
        public int Count => source.Count;
        public Point2 this[int i]
        {
            get
            {
                Reads++;
                var value = source[i];
                if (i == index) cancellation.Cancel();
                return value;
            }
        }
        public IEnumerator<Point2> GetEnumerator() => source.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
