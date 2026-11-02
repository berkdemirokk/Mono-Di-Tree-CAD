using System.Reflection;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

// A coverage prerequisite only: not a garment-feature or production certificate.
public sealed class OffsetConcaveWitnessCoverageTests
{
    private static bool Covers(GridPoint[] points, int[] witnesses)
    {
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetConcaveWitnessCoverage", true)!
            .GetMethod("TryVerify", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [points, witnesses, null, CancellationToken.None];
        return (bool)method.Invoke(null, args)!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingRepeatedOrUnrelatedWitnessIsRejected(bool reverse)
    {
        GridPoint[] source = [new(0,0),new(100,0),new(100,100),new(70,100),
            new(70,60),new(30,60),new(30,100),new(0,100)];
        var before = source.ToArray();
        if (reverse) Array.Reverse(source);
        int a = reverse ? 3 : 4, b = reverse ? 2 : 5;
        Assert.True(Covers(source, [a,b]));
        Assert.False(Covers(source, [a]));
        Assert.False(Covers(source, [a,a,b]));
        Assert.False(Covers(source, [a,b,0]));
        if (reverse) Array.Reverse(source);
        Assert.Equal(before, source);
    }

    [Fact]
    public void AmbiguousCollinearTurnCannotBeDeclaredFullyCovered()
    {
        GridPoint[] source = [new(0,0),new(50,0),new(100,0),new(100,100),new(0,100)];
        Assert.False(Covers(source, []));
    }

    [Fact]
    public void OuterPocketWitnessesCannotHideMissingInnerNotchTurns()
    {
        GridPoint[] source = [new(0,0),new(100,0),new(100,100),new(80,100),
            new(80,60),new(65,60),new(65,50),new(55,50),new(55,60),
            new(20,60),new(20,100),new(0,100)];
        Assert.True(Covers(source, [4,6,7,9]));
        Assert.False(Covers(source, [4,9]));
        Assert.False(Covers(source, [4,6,9]));
    }
}
