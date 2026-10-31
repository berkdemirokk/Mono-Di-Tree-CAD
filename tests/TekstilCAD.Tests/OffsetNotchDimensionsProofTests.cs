using System.Reflection;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetNotchDimensionsProofTests
{
    [Theory]
    [InlineData(false, 0L)]
    [InlineData(true, 0L)]
    [InlineData(false, 50000000000000L)]
    [InlineData(true, -50000000000000L)]
    public void IndependentlyEnforcesSourceAndCandidateMouthWidths(bool reverse, long origin)
    {
        GridPoint[] Contour(long width) =>
        [new(origin,origin), new(origin+100,origin), new(origin+100,origin+100),
         new(origin+50+width/2,origin+100), new(origin+50+width/2,origin+70),
         new(origin+50-width/2,origin+70), new(origin+50-width/2,origin+100), new(origin,origin+100)];
        var source = Contour(40); var candidate = Contour(20);
        if (reverse) { Array.Reverse(source); Array.Reverse(candidate); }
        var before = source.ToArray();
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetNotchDimensionsProof", true)!
            .GetMethod("TryValidate", BindingFlags.NonPublic | BindingFlags.Static)!;
        bool Validate(long sw, long cw, long depth = 30)
        {
            object?[] args = [source, candidate, reverse ? 1 : 3, reverse ? 4 : 6,
                depth, depth, sw, cw, null, CancellationToken.None];
            return (bool)method.Invoke(null, args)!;
        }
        Assert.True(Validate(40,20));
        Assert.False(Validate(41,20));
        Assert.False(Validate(40,21));
        Assert.False(Validate(40,20,31));
        Assert.False(Validate(0,20));
        Assert.Equal(before, source);
    }
}
