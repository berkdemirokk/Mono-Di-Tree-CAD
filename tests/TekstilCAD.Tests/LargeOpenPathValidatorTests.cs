using System.Collections;
using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public sealed class LargeOpenPathValidatorTests
{
    private static List<Point2> Arc(int count, double origin = 0) => Enumerable.Range(0, count).Select(i =>
    {
        var angle = Math.Tau * i / count;
        return new Point2(origin + 1000 * Math.Cos(angle), origin + 1000 * Math.Sin(angle));
    }).ToList();

    private static bool Accepts(IReadOnlyList<Point2> points)
    {
        try { LargeOpenPathValidator.Validate(points); return true; }
        catch (ArgumentException) { return false; }
    }

    [Fact]
    public void SeededPathsAgreeWithExistingOpenPathPredicate()
    {
        var random = new Random(78214);
        foreach (var origin in new[] { 0.0, 50_000_000.0, -99_980_000.0 })
        for (var trial = 0; trial < 60; trial++)
        {
            var count = 5 + random.Next(40);
            var points = Enumerable.Range(0, count).Select(i =>
                new Point2(origin + i * 3, origin + random.NextDouble() * 10)).ToList();
            if (trial % 3 == 0) (points[1], points[count - 2]) = (points[count - 2], points[1]);
            if (trial % 5 == 0) points.Reverse();
            var before = points.ToArray();
            Assert.Equal(!Geometry.HasSelfIntersections(points, false), Accepts(points));
            Assert.Equal(before, points);
        }
    }

    [Fact]
    public void ZeroLengthAdjacentOverlapAndCrossingAreRejected()
    {
        var cases = new List<List<Point2>>
        {
            new() { new(0, 0), new(0, 0) },
            new() { new(0, 0), new(4, 0), new(1, 0) },
            new() { new(0, 0), new(4, 4), new(0, 4), new(4, 0) }
        };
        foreach (var points in cases)
        {
            var before = points.ToArray();
            Assert.True(Geometry.HasSelfIntersections(points, false));
            Assert.ThrowsAny<ArgumentException>(() => LargeOpenPathValidator.Validate(points));
            Assert.Equal(before, points);
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(50000000.0)]
    public void NearEpsilonSegmentsAgreeWithExistingPredicate(double origin)
    {
        foreach (var length in new[] { Geometry.Epsilon / 2, Geometry.Epsilon, Geometry.Epsilon * 2, .000001 })
        {
            List<Point2> points = [new(origin, origin), new(origin + length, origin), new(origin + 1, origin + 1)];
            var before = points.ToArray();
            Assert.Equal(!Geometry.HasSelfIntersections(points, false), Accepts(points));
            Assert.Equal(before, points);
        }
    }

    [Theory]
    [InlineData(4097, 0.0)]
    [InlineData(10000, 50000000.0)]
    public void LargeValidOpenPathIsAcceptedWithoutMutation(int count, double origin)
    {
        var points = Arc(count, origin);
        var before = points.ToArray();
        LargeOpenPathValidator.Validate(points);
        Assert.Equal(before, points);
    }

    [Fact]
    public void CancellationDuringSnapshotLeavesInputUnchanged()
    {
        var points = Arc(4097);
        var before = points.ToArray();
        using var cancellation = new CancellationTokenSource();
        var source = new CancelDuringRead(points, cancellation);
        Assert.ThrowsAny<OperationCanceledException>(() => LargeOpenPathValidator.Validate(source, cancellation.Token));
        Assert.True(source.Reads > 1000);
        Assert.Equal(before, points);
    }

    private sealed class CancelDuringRead(IReadOnlyList<Point2> points, CancellationTokenSource cancellation) : IReadOnlyList<Point2>
    {
        public int Reads { get; private set; }
        public int Count => points.Count;
        public Point2 this[int index]
        {
            get
            {
                Reads++;
                var point = points[index];
                if (index == 1500) cancellation.Cancel();
                return point;
            }
        }
        public IEnumerator<Point2> GetEnumerator() => points.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
