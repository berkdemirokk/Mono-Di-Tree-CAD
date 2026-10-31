using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>Compares candidate pruning with exhaustive original-order pair evaluation.
/// This verifies pruning, not the underlying geometric predicate's mathematical exactness.</summary>
public sealed class DxfLineSweepEquivalenceTests
{
    private static readonly MethodInfo Sweep = typeof(DxfFormat).GetMethod("HasAmbiguousLineConnections", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo Pair = typeof(DxfFormat).GetMethod("AmbiguousLinePair", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static bool Pruned(List<(Point2 A, Point2 B)> edges) => (bool)Sweep.Invoke(null, [edges])!;
    private static bool Brute(List<(Point2 A, Point2 B)> edges)
    {
        var matches = new byte[edges.Count * 2];
        for (var i = 0; i < edges.Count; i++)
            for (var j = i + 1; j < edges.Count; j++)
                if ((bool)Pair.Invoke(null, [edges, matches, i, j])!) return true;
        return false;
    }

    [Fact]
    public void SweepMatchesAllPairsForDeterministicMixedGroupsAndReordering()
    {
        var random = new Random(789425);
        for (var sample = 0; sample < 2500; sample++)
        {
            var edges = new List<(Point2 A, Point2 B)>();
            var count = random.Next(0, 19);
            for (var i = 0; i < count; i++)
            {
                Point2 Pick()
                {
                    var x = random.Next(-8,9); var y = random.Next(-8,9);
                    var shift = random.Next(0, 12) switch { 0 => 0.000001, 1 => 0.0000010000001, 2 => 0.0000009999999, _ => 0 };
                    return new Point2(x + shift, y - shift);
                }
                var a = Pick(); var b = Pick();
                if (edges.Count > 0 && random.Next(3) == 0) a = edges[random.Next(edges.Count)].B;
                edges.Add((a,b));
            }
            Assert.Equal(Brute(edges), Pruned(edges));
            edges.Reverse();
            Assert.Equal(Brute(edges), Pruned(edges));
        }
    }

    [Fact]
    public void NearThresholdsLargeOriginsAndOverlapAgreeWithExhaustivePairs()
    {
        foreach (var origin in new[] { 0.0, 99_000_000.0, -99_000_000.0 })
        foreach (var displacement in new[] { 0.0, 0.0000009, 0.000001, 0.0000011 })
        {
            List<(Point2 A, Point2 B)> edges = [
                (new(origin,origin), new(origin+10,origin)),
                (new(origin+10+displacement,origin), new(origin+20,origin)),
                (new(origin+5,origin-5),new(origin+5,origin+5)),
                (new(origin+4,origin+20),new(origin+12,origin+20))];
            Assert.Equal(Brute(edges),Pruned(edges));
            edges.Reverse(); Assert.Equal(Brute(edges),Pruned(edges));
        }
    }

    [Fact]
    public void MaximumDisjointAndDenseBoundingRangesAreBoundedWithoutLosingEdges()
    {
        // 4096 is the existing import-wide LINE cap. No timing assertion is
        // made: runtime depends on the machine. Both cases have no contacts.
        var separated = Enumerable.Range(0,4096).Select(i => (new Point2(i*3.0,0),new Point2(i*3.0+1,0))).ToList();
        var overlappingX = Enumerable.Range(0,4096).Select(i => (new Point2(0,i*3.0),new Point2(10000,i*3.0))).ToList();
        Assert.False(Pruned(separated));
        Assert.False(Pruned(overlappingX));
    }
}
