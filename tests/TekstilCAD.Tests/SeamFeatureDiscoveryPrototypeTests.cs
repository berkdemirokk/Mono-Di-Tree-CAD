using System.Reflection;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

// Research only. An exhaustive proximity inventory is not a semantic feature
// detector, a simple-polygon check, or an offset acceptance criterion.
public sealed class SeamFeatureDiscoveryPrototypeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FindsNarrowNotchInEitherWinding(bool reverse)
    {
        GridPoint[] points = [new(0, 0), new(100, 0), new(100, 100), new(55, 100),
            new(55, 50), new(45, 50), new(45, 100), new(0, 100)];
        if (reverse) Array.Reverse(points);
        Assert.True(TryDiscover(points, 11, 100, out var pairs));
        int first = reverse ? 1 : 3, second = reverse ? 3 : 5;
        Assert.Contains((first, second), pairs);
        Assert.True(TryDiscover(points, 10, 100, out pairs));
        Assert.DoesNotContain((first, second), pairs); // distance equal to threshold
    }

    [Fact]
    public void FindsCrossingsAndNestedNarrowPairs()
    {
        GridPoint[] crossing = [new(0, 0), new(10, 10), new(0, 10), new(10, 0)];
        Assert.True(TryDiscover(crossing, 1, 10, out var pairs));
        Assert.Contains((0, 2), pairs);
        GridPoint[] nested = [new(0, 0), new(100, 0), new(100, 100), new(80, 100),
            new(80, 60), new(60, 60), new(60, 55), new(50, 55),
            new(50, 60), new(20, 60), new(20, 100), new(0, 100)];
        Assert.True(TryDiscover(nested, 11, 100, out pairs));
        Assert.Contains((5, 7), pairs);
    }

    [Fact]
    public void InvalidInputOrInsufficientBudgetNeverReturnsPartialInventory()
    {
        GridPoint[] square = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
        Assert.False(TryDiscover(square, 11, 1, out var pairs));
        Assert.Empty(pairs);
        square[1] = square[0];
        Assert.False(TryDiscover(square, 11, 10, out pairs));
        Assert.Empty(pairs);
        Assert.False(TryDiscover(square, 0, 10, out pairs));
        Assert.Empty(pairs);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => TryDiscover(square, 1, 10, out _, cts.Token));
    }

    // Returns every unordered, nonadjacent edge pair with distance strictly less
    // than threshold, including intersections. Max 512 vertices and a caller
    // supplied pair budget bound work. False means no usable inventory; empty
    // on success means no pair met this definition, not that no feature exists.
    private static bool TryDiscover(IReadOnlyList<GridPoint> contour, long threshold,
        int pairBudget, out IReadOnlyList<(int First, int Second)> pairs,
        CancellationToken token = default)
    {
        pairs = Array.Empty<(int, int)>();
        token.ThrowIfCancellationRequested();
        if (contour is null || contour.Count < 4 || contour.Count > 512 || threshold <= 0 || pairBudget < 0)
            return false;
        int n = contour.Count;
        for (int i = 0; i < n; i++)
            if (contour[i].X == contour[(i + 1) % n].X && contour[i].Y == contour[(i + 1) % n].Y)
                return false;
        long required = (long)n * (n - 3) / 2;
        if (required > pairBudget) return false;
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetFeatureWidthDiagnostic", true)!
            .GetMethod("HasMinimumWidth", BindingFlags.NonPublic | BindingFlags.Static)!;
        var found = new List<(int, int)>();
        for (int i = 0; i < n; i++)
        for (int j = i + 1; j < n; j++)
        {
            token.ThrowIfCancellationRequested();
            if (j == i + 1 || (i == 0 && j == n - 1)) continue;
            if (!(bool)method.Invoke(null, [contour, i, j, threshold, token])!) found.Add((i, j));
        }
        pairs = found;
        return true;
    }
}
