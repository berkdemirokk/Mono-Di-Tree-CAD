using System.Reflection;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Documents why an aggregate witness cannot authorize an offset.</summary>
public sealed class OffsetNestedNotchWitnessGapTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LargePocketWitnessDoesNotProveNestedNotchDepth(bool reverse)
    {
        GridPoint[] Make(long innerDepth) =>
        [new(0, 0), new(100, 0), new(100, 100), new(80, 100),
         new(80, 60), new(65, 60), new(65, 60-innerDepth),
         new(55, 60-innerDepth), new(55, 60), new(20, 60),
         new(20, 100), new(0, 100)];
        var source = Make(10);
        var candidate = Make(1);
        if (reverse) { Array.Reverse(source); Array.Reverse(candidate); }
        var before = source.ToArray();
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetNotchWitness", true)!
            .GetMethod("TryValidate", BindingFlags.NonPublic | BindingFlags.Static)!;
        bool Validate(int start, int end, long sourceDepth, long candidateDepth)
        {
            if (reverse) (start, end) = (source.Length - 1 - end, source.Length - 1 - start);
            object?[] args = [source, candidate, start, end, sourceDepth, candidateDepth, null, CancellationToken.None];
            return (bool)method.Invoke(null, args)!;
        }
        // The outer witness only measures maximum depth; the nested feature
        // must be selected separately to detect the ten-to-one shrinkage.
        Assert.True(Validate(3, 10, 40, 40));
        Assert.False(Validate(5, 8, 10, 10));
        Assert.Equal(before, source);
    }
}
