using System.Reflection;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetAllTurnsEvidenceTests
{
    private static bool Verify(GridPoint[] source, GridPoint[] candidate, long width, long depth)
    {
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetAllTurnsEvidence", true)!
            .GetMethod("TryVerify", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] args = [source,candidate,width,depth,null,CancellationToken.None];
        return (bool)method.Invoke(null,args)!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllTurnsIncludingConcaveAndFlatAreCompared(bool reverse)
    {
        GridPoint[] source = [new(0,0),new(50,0),new(100,0),new(100,100),new(70,100),
            new(70,60),new(30,60),new(30,100),new(0,100)];
        if (reverse) Array.Reverse(source);
        var candidate = source.Select(p => new GridPoint(p.X+200,p.Y-300)).ToArray();
        Assert.True(Verify(source,candidate,0,0));
        candidate[4] = candidate[4] with { Y = candidate[4].Y - 1 };
        Assert.False(Verify(source,candidate,0,0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneGridRecessCannotBeFlattenedEvenWithLargeNumericTolerance(bool reverse)
    {
        GridPoint[] source = [new(0,0),new(100,0),new(100,100),new(51,100),
            new(51,99),new(50,99),new(50,100),new(0,100)];
        var candidate = source.ToArray();
        candidate[4] = candidate[4] with { Y = 100 };
        candidate[5] = candidate[5] with { Y = 100 };
        if (reverse) { Array.Reverse(source); Array.Reverse(candidate); }
        Assert.False(Verify(source,candidate,100,100));
    }

    [Fact]
    public void BroadBudgetCanStillAllowAOneGridDepthReduction()
    {
        GridPoint[] source = [new(0,0),new(100,0),new(100,100),new(51,100),
            new(51,98),new(50,98),new(50,100),new(0,100)];
        var candidate = source.ToArray();
        candidate[4] = candidate[4] with { Y = 99 };
        candidate[5] = candidate[5] with { Y = 99 };
        Assert.True(Verify(source,candidate,8,8));
        Assert.False(Verify(source,candidate,0,0));
    }

    [Fact]
    public void IndexShiftAndDegenerateChordAreNotCertified()
    {
        GridPoint[] source = [new(0,0),new(100,0),new(100,100),new(70,100),new(70,60),new(30,60),new(30,100),new(0,100)];
        var shifted = source.Skip(1).Concat(source.Take(1)).ToArray();
        Assert.False(Verify(source,shifted,0,0));
        var candidate=source.ToArray(); candidate[0]=candidate[2];
        Assert.False(Verify(source,candidate,100,100));
    }

    [Fact]
    public void SymmetricContoursDemonstrateThatLocalMetricsDoNotProveMapping()
    {
        GridPoint[] square = [new(0,0),new(100,0),new(100,100),new(0,100)];
        var shifted = square.Skip(1).Concat(square.Take(1)).ToArray();
        // Every local chord and height agrees exactly, although indices refer
        // to different corners. A separate boundary mapping proof is mandatory.
        Assert.True(Verify(square,shifted,0,0));
    }
}
