using System.Collections;
using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public sealed class LargeContourSweepEquivalenceTests
{
    private static void Compare(IReadOnlyList<Point2> points, string label)
    {
        var before = points.ToArray();
        var oracle = Record.Exception(() => Geometry.ValidateContour(points));
        var actual = Record.Exception(() => LargeContourValidator.Validate(points));
        Assert.True((oracle is null) == (actual is null),
            $"{label}: Geometry={oracle?.GetType().Name ?? "accepted"}, large={actual?.GetType().Name ?? "accepted"}");
        if (oracle is not null) Assert.Equal(oracle.GetType(), actual!.GetType());
        Assert.Equal(before, points);
    }

    [Fact]
    public void DeterministicCombAndEdgeContactsMatchOracleInBothDirections()
    {
        var comb = new List<Point2> { new(0, 0), new(240, 0), new(240, 100) };
        for (var x = 238; x >= 2; x -= 4)
        {
            comb.Add(new(x, 100)); comb.Add(new(x, 10));
            comb.Add(new(x - 2, 10)); comb.Add(new(x - 2, 100));
        }

        var cases = new List<Point2[]>
        {
            comb.ToArray(),
            new Point2[] { new(0,0), new(40,0), new(40,40), new(20,40), new(20,0), new(10,20), new(0,40) },
            new Point2[] { new(0,0), new(40,0), new(40,40), new(10,40), new(10,0), new(30,0), new(0,40) },
            new Point2[] { new(0,0), new(40,0), new(20,0), new(40,40), new(0,40) },
            new Point2[] { new(0,0), new(40,0), new(40,40), new(0,40), new(40,0) }
        };
        for (var i = 0; i < cases.Count; i++)
        {
            var contour = cases[i];
            Compare(contour, $"case {i}");
            Compare(contour.Reverse().ToArray(), $"reversed {i}");
            Compare(contour.Concat([contour[0]]).ToArray(), $"closed {i}");
        }
    }

    [Fact]
    public void SeededPermutationsAndNearCoincidentEdgesMatchOracle()
    {
        var random = new Random(417832);
        for (var trial = 0; trial < 80; trial++)
        {
            var count = 30 + trial % 30;
            var points = Enumerable.Range(0, count).Select(i =>
            {
                var angle = Math.Tau * i / count;
                var radius = 100 + random.NextDouble() * 50;
                return new Point2(radius * Math.Cos(angle), radius * Math.Sin(angle));
            }).ToArray();
            if (trial % 2 == 0)
            {
                var a = random.Next(count); var b = (a + count / 2) % count;
                (points[a], points[b]) = (points[b], points[a]);
            }
            Compare(points, $"seeded {trial}");
        }
        foreach (var gap in new[] { -1e-7, -1e-8, 0.0, 1e-8, 1e-7 })
        foreach (var origin in new[] { 0.0, 50_000_000.0 })
        {
            Point2[] points = [new(origin, origin), new(origin + 100, origin),
                new(origin + 100, origin + 50), new(origin + 51, origin + 50),
                new(origin + 51, origin + gap), new(origin + 49, origin + gap),
                new(origin + 49, origin + 50), new(origin, origin + 50)];
            Compare(points, $"gap {gap}, origin {origin}");
        }
    }

    [Fact]
    public void CancellationAfterInputReadingHasStartedLeavesInputReusable()
    {
        var points = Enumerable.Range(0, 3000).Select(i =>
        {
            var angle = Math.Tau * i / 3000;
            return new Point2(1000 * Math.Cos(angle), 1000 * Math.Sin(angle));
        }).ToArray();
        var before = points.ToArray();
        using var cancellation = new CancellationTokenSource();
        var observed = new CancelDuringRead(points, cancellation);
        Assert.ThrowsAny<OperationCanceledException>(() => LargeContourValidator.Validate(observed, cancellation.Token));
        Assert.True(observed.Reads >= 1000);
        Assert.Equal(before, points);
        Compare(points, "after cancellation");
    }

    private sealed class CancelDuringRead(Point2[] points, CancellationTokenSource cancellation) : IReadOnlyList<Point2>
    {
        public int Reads { get; private set; }
        public int Count => points.Length;
        public Point2 this[int index]
        {
            get
            {
                if (++Reads == 1000) cancellation.Cancel();
                return points[index];
            }
        }
        public IEnumerator<Point2> GetEnumerator() => ((IEnumerable<Point2>)points).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
