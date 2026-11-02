using System.Reflection;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetRectangularNotchPipelineTests
{
    private static (bool Ok, string Reason) Verify(GridPoint[] source, GridPoint[] candidate, long width, long depth)
    {
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetRectangularNotchPipeline", true)!
            .GetMethod("TryVerify", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [source, candidate, 1_000_000L, width, depth, null, CancellationToken.None];
        return ((bool)method.Invoke(null, args)!, (string)args[5]!);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 3)]
    [InlineData(true, 5)]
    public void IndexedMiterRequiresEveryPocketAndExplicitDimensionalTolerance(bool reverse, int start)
    {
        const long u = 1_000_000;
        GridPoint[] source = [new(0,0),new(100*u,0),new(100*u,100*u),new(70*u,100*u),
            new(70*u,60*u),new(30*u,60*u),new(30*u,100*u),new(0,100*u)];
        GridPoint[] candidate = [new(-u,-u),new(101*u,-u),new(101*u,101*u),new(69*u,101*u),
            new(69*u,61*u),new(31*u,61*u),new(31*u,101*u),new(-u,101*u)];
        if (reverse) { Array.Reverse(source); Array.Reverse(candidate); }
        source = source.Skip(start).Concat(source.Take(start)).ToArray();
        candidate = candidate.Skip(start).Concat(candidate.Take(start)).ToArray();
        var before = source.ToArray();
        var result = Verify(source, candidate, 2*u, 2*u);
        Assert.True(result.Ok, result.Reason);
        Assert.False(Verify(source, candidate, 2*u-1, 2*u).Ok);
        var shifted = candidate.Skip(1).Concat(candidate.Take(1)).ToArray();
        Assert.False(Verify(source, shifted, 2*u, 2*u).Ok);
        var changed = candidate.ToArray(); changed[0] = changed[0] with { X = changed[0].X + 1 };
        Assert.False(Verify(source, changed, 2*u, 2*u).Ok);
        Assert.Equal(before, source);
    }

    [Fact]
    public void LostOneGridPocketCannotBeCertified()
    {
        const long u = 1_000_000;
        GridPoint[] source = [new(0,0),new(100*u,0),new(100*u,100*u),new(50*u+1,100*u),
            new(50*u+1,100*u-1),new(50*u,100*u-1),new(50*u,100*u),new(0,100*u)];
        var flattened = source.ToArray();
        flattened[4] = flattened[4] with { Y = 100*u };
        flattened[5] = flattened[5] with { Y = 100*u };
        Assert.False(Verify(source, flattened, 2*u, 2*u).Ok);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneOfTwoPocketsCannotDisappearOrBeMatchedToTheOther(bool reverse)
    {
        const long u = 1_000_000;
        GridPoint[] source = [new(0,0),new(100*u,0),new(100*u,100*u),new(80*u,100*u),new(80*u,70*u),
            new(70*u,70*u),new(70*u,100*u),new(50*u,100*u),new(50*u,80*u),new(40*u,80*u),
            new(40*u,100*u),new(0,100*u)];
        GridPoint[] candidate = [new(-u,-u),new(101*u,-u),new(101*u,101*u),new(79*u,101*u),new(79*u,71*u),
            new(71*u,71*u),new(71*u,101*u),new(49*u,101*u),new(49*u,81*u),new(41*u,81*u),
            new(41*u,101*u),new(-u,101*u)];
        if (reverse) { Array.Reverse(source); Array.Reverse(candidate); }
        var result = Verify(source, candidate, 2*u, 2*u);
        Assert.True(result.Ok, result.Reason);
        var lost = candidate.ToArray();
        int bottomA = reverse ? 7 : 4, bottomB = reverse ? 6 : 5;
        lost[bottomA] = lost[bottomA] with { Y = 101*u };
        lost[bottomB] = lost[bottomB] with { Y = 101*u };
        Assert.False(Verify(source, lost, 2*u, 2*u).Ok);
        var swapped = candidate.ToArray();
        int left = reverse ? 5 : 3, right = reverse ? 1 : 7;
        for (int i = 0; i < 4; i++) (swapped[(left+i)%12],swapped[(right+i)%12]) = (swapped[(right+i)%12],swapped[(left+i)%12]);
        Assert.False(Verify(source, swapped, 2*u, 2*u).Ok);
    }

    [Fact]
    public void UnsupportedNestedPocketFailsClosed()
    {
        GridPoint[] source = [new(0,0),new(100,0),new(100,100),new(80,100),new(80,60),
            new(65,60),new(65,50),new(55,50),new(55,60),new(20,60),new(20,100),new(0,100)];
        Assert.False(Verify(source, source, 2, 2).Ok);
    }
}
