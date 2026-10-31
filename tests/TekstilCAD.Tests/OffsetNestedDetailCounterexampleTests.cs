using System.Reflection;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Counterexample to using the ordered distance certificate as a detail proof.</summary>
public sealed class OffsetNestedDetailCounterexampleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedRecessCanDisappearDespiteVertexByVertexOrderedMatch(bool reverse)
    {
        // The outer recess remains, but its one-grid-unit inner recess disappears.
        // Every vertex keeps its index and moves at most one grid unit. This is
        // an exact grid example, independent of floating-point rounding.
        GridPoint[] reference = [new(0,0), new(100,0), new(100,100),
            new(60,100), new(56,98), new(52,98), new(50,97),
            new(48,98), new(44,98), new(40,100), new(0,100)];
        var actual = (GridPoint[])reference.Clone();
        actual[6] = new(50,98);
        if (reverse) { Array.Reverse(reference); Array.Reverse(actual); }
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetOrderedBoundaryCertificate", true)!
            .GetMethod("TryVerify", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [reference, actual, 1L, null, CancellationToken.None];
        Assert.True((bool)method.Invoke(null, args)!);
    }
}
