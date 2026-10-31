using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Experimental source-pocket discovery; these tests do not authorize an offset.</summary>
public sealed class OffsetPocketDiscoveryTests
{
    private sealed record Pocket(int[] Indices, int Depth, int[] ConcaveVertices);
    private static (bool Accepted, Pocket[] Pockets, string Reason) Discover(
        IReadOnlyList<GridPoint> points, CancellationToken token = default)
    {
        var type = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetPocketDiscovery", true)!;
        var method = type.GetMethod("TryDiscover", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] args = [points, null, null, token];
        try
        {
            bool accepted = (bool)method.Invoke(null, args)!;
            var pockets = ((IEnumerable)args[1]!).Cast<object>().Select(p =>
            {
                var t = p.GetType();
                var indices = ((IEnumerable)t.GetProperty("SourceIndices")!.GetValue(p)!).Cast<int>().ToArray();
                var concave = ((IEnumerable)t.GetProperty("OriginalConcaveVertices")!.GetValue(p)!).Cast<int>().ToArray();
                return new Pocket(indices, (int)t.GetProperty("Depth")!.GetValue(p)!, concave);
            }).ToArray();
            return (accepted, pockets, (string)args[2]!);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConvexContourHasNoPocketsAndSourceIsUnchanged(bool reverse)
    {
        var source = new List<GridPoint> { new(0, 0), new(10, 0), new(10, 10), new(0, 10) };
        if (reverse) source.Reverse();
        var before = source.ToArray();
        var result = Discover(source);
        Assert.True(result.Accepted, result.Reason);
        Assert.Empty(result.Pockets);
        Assert.Equal(before, source);
        Assert.Contains("üretim uygunluğu kanıtı değildir", result.Reason);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void OneConcaveCornerIsCoveredForBothWindingsAndCyclicStarts(bool reverse, int shift)
    {
        var source = new List<GridPoint> { new(0, 0), new(6, 0), new(6, 2), new(2, 2), new(2, 6), new(0, 6) };
        if (reverse) source.Reverse();
        source = source.Skip(shift).Concat(source.Take(shift)).ToList();
        var before = source.ToArray();
        var concave = source.IndexOf(new GridPoint(2, 2));
        var result = Discover(source);
        Assert.True(result.Accepted, result.Reason);
        var pocket = Assert.Single(result.Pockets);
        Assert.Contains(concave, pocket.ConcaveVertices);
        Assert.Contains(concave, pocket.Indices);
        Assert.True(pocket.Depth >= 1);
        Assert.All(pocket.Indices, index => Assert.InRange(index, 0, source.Count - 1));
        Assert.Equal(before, source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeparateOppositeIndentationsRemainSeparateSourcePockets(bool reverse)
    {
        var source = new List<GridPoint> { new(0, 0), new(10, 0), new(8, 5), new(10, 10), new(0, 10), new(2, 5) };
        if (reverse) source.Reverse();
        var result = Discover(source);
        Assert.True(result.Accepted, result.Reason);
        Assert.Equal(2, result.Pockets.Length);
        var a = source.IndexOf(new GridPoint(8, 5));
        var b = source.IndexOf(new GridPoint(2, 5));
        Assert.Single(result.Pockets, p => p.ConcaveVertices.Contains(a));
        Assert.Single(result.Pockets, p => p.ConcaveVertices.Contains(b));
        Assert.DoesNotContain(result.Pockets, p => p.ConcaveVertices.Contains(a) && p.ConcaveVertices.Contains(b));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALeafCanContainMultipleConcaveVerticesAndDoesNotProveSeparateFeatures(bool reverse)
    {
        var source = new List<GridPoint> { new(0, 0), new(10, 0), new(10, 10), new(7, 7),
            new(5, 6), new(3, 7), new(0, 10) };
        if (reverse) source.Reverse();
        var before = source.ToArray();
        var result = Discover(source);
        Assert.True(result.Accepted, result.Reason);
        var pocket = Assert.Single(result.Pockets);
        foreach (var point in new[] { new GridPoint(7, 7), new GridPoint(5, 6), new GridPoint(3, 7) })
        {
            var index = source.IndexOf(point);
            Assert.Contains(index, pocket.Indices);
            Assert.Contains(index, pocket.ConcaveVertices);
        }
        // A leaf is a hull-decomposition region, not proof that each concave
        // vertex is a separate notch or that any feature dimensions survive.
        Assert.Equal(before, source);
    }

    [Fact]
    public void NestedIrregularPocketNeverReturnsUncoveredConcaveVertices()
    {
        // Two indentations inside a larger upper recess. Its decomposition may
        // be ambiguous; acceptance must still cover every independently computed concave vertex.
        var source = new List<GridPoint> { new(0, 0), new(12, 0), new(12, 12), new(9, 10),
            new(10, 8), new(7, 7), new(6, 9), new(3, 8), new(0, 12) };
        var before = source.ToArray();
        var result = Discover(source);
        if (result.Accepted)
        {
            var covered = result.Pockets.SelectMany(p => p.ConcaveVertices).ToHashSet();
            // This fixture is counterclockwise, so negative cross products are concave.
            for (var i = 0; i < source.Count; i++)
            {
                var a = source[(i + source.Count - 1) % source.Count]; var b = source[i]; var c = source[(i + 1) % source.Count];
                var cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
                if (cross < 0) Assert.Contains(i, covered);
            }
        }
        else Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        Assert.Equal(before, source);
    }

    [Fact]
    public void CollinearTangentAndSelfIntersectingInputsAreRejected()
    {
        GridPoint[][] invalid = [
            [new(0,0), new(5,0), new(10,0), new(10,10), new(0,10)],
            [new(0,0), new(10,0), new(10,10), new(7,10), new(5,6), new(3,10), new(0,10)],
            [new(0,0), new(10,10), new(0,10), new(10,0)],
            [new(0,0), new(10,0), new(10,10), new(0,0)]
        ];
        foreach (var source in invalid)
        {
            var before = source.ToArray();
            var result = Discover(source);
            Assert.False(result.Accepted);
            Assert.Empty(result.Pockets);
            Assert.False(string.IsNullOrWhiteSpace(result.Reason));
            Assert.Equal(before, source);
        }
    }

    [Fact]
    public void VertexLimitAndCancellationAreEnforced()
    {
        var tooMany = Enumerable.Range(0, 1025).Select(i => new GridPoint(i, i * (long)i)).ToArray();
        Assert.False(Discover(tooMany).Accepted);
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Discover([new(0, 0), new(1, 0), new(0, 1)], source.Token));
    }
}
