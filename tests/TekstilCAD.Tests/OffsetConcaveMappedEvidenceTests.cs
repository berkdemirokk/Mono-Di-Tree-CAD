using System.Reflection;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

// These tests cover indexed local triples, not complete garment features.
public sealed class OffsetConcaveMappedEvidenceTests
{
    [Fact]
    public void SweptBoxSeparationNeverTreatsContactOrCrossingAsDisjoint()
    {
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetLinearTopologyExperiment", true)!
            .GetMethod("SweptBoxesDisjoint", BindingFlags.NonPublic | BindingFlags.Static)!;
        bool Apart(GridPoint[] p) => (bool)method.Invoke(null, p.Cast<object>().ToArray())!;
        // Two vertical edges swap sides during the motion; endpoint snapshots
        // are separated but their swept boxes overlap.
        Assert.False(Apart([new(0,0),new(10,0),new(0,2),new(10,2),
            new(10,0),new(0,0),new(10,2),new(0,2)]));
        Assert.False(Apart([new(0,0),new(0,0),new(0,2),new(0,2),
            new(0,2),new(0,2),new(0,4),new(0,4)]));
        Assert.True(Apart([new(0,0),new(1,0),new(0,2),new(1,2),
            new(3,0),new(4,0),new(3,2),new(4,2)]));
    }

    private static (bool Ok, string Reason) Verify(GridPoint[] source, GridPoint[] candidate,
        (int Start, int Witness, int End, double WidthMm, double DepthMm)[] features)
    {
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetConcaveMappedEvidence", true)!
            .GetMethod("TryVerify", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [source, candidate, 1_000_000L, features, null, CancellationToken.None];
        return ((bool)method.Invoke(null, args)!, (string)args[4]!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsMissingDuplicateAndNonAdjacentMatches(bool reverse)
    {
        const long u = 1_000_000;
        GridPoint[] source = [new(0,0),new(100*u,0),new(100*u,100*u),new(70*u,100*u),
            new(70*u,60*u),new(30*u,60*u),new(30*u,100*u),new(0,100*u)];
        GridPoint[] candidate = [new(-u,-u),new(101*u,-u),new(101*u,101*u),new(69*u,101*u),
            new(69*u,61*u),new(31*u,61*u),new(31*u,101*u),new(-u,101*u)];
        if (reverse) { Array.Reverse(source); Array.Reverse(candidate); }
        var before = source.ToArray();
        int a = reverse ? 3 : 4, b = reverse ? 2 : 5, n = source.Length;
        (int, int, int, double, double) Feature(int i) => ((i+n-1)%n,i,(i+1)%n,10,10);
        var fa = Feature(a); var fb = Feature(b);
        var valid = Verify(source, candidate, [fa,fb]);
        Assert.True(valid.Ok, valid.Reason);
        Assert.False(Verify(source, candidate, [fa]).Ok);
        Assert.False(Verify(source, candidate, [fa,fb,fa]).Ok);
        Assert.False(Verify(source, candidate, [((a+n-2)%n,a,(a+1)%n,10,10),fb]).Ok);
        Assert.False(Verify(source, candidate, [((a+n-1)%n,a,(a+1)%n,0,0),fb]).Ok);
        var altered = candidate.ToArray(); altered[a] = altered[a] with { X = altered[a].X + 1 };
        Assert.False(Verify(source, altered, [fa,fb]).Ok);
        Assert.Equal(before, source);
    }
}
