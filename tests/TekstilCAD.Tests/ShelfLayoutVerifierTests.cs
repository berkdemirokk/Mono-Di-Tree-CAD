using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public class ShelfLayoutVerifierTests
{
    private static PatternPiece Box(double x, double y, double w = 10, double h = 10) => new()
    {
        Points = [new(x,y), new(x+w,y), new(x+w,y+h), new(x,y+h)]
    };

    private static NestingResult Layout(double gap, params PatternPiece[] pieces) =>
        new(new ProjectDocument { Pieces = pieces.ToList() }, 30, 0, 0)
        { FabricWidth = 30, Gap = gap, EdgeMargin = 1 };

    [Fact]
    public void AcceptsClearBoxesAndZeroGapContact()
    {
        ShelfLayoutVerifier.Verify(Layout(2, Box(1,1), Box(13,1)));
        ShelfLayoutVerifier.Verify(Layout(0, Box(1,1), Box(11,1)));
    }

    [Fact]
    public void RejectsOverlapAndGapViolation()
    {
        Assert.Throws<InvalidOperationException>(() => ShelfLayoutVerifier.Verify(Layout(0, Box(1,1), Box(10,1))));
        Assert.Throws<InvalidOperationException>(() => ShelfLayoutVerifier.Verify(Layout(2, Box(1,1), Box(12,1))));
    }

    [Fact]
    public void RejectsFabricBoundaryAndNonfiniteCoordinates()
    {
        Assert.Throws<InvalidOperationException>(() => ShelfLayoutVerifier.Verify(Layout(0, Box(0,1))));
        Assert.Throws<InvalidOperationException>(() => ShelfLayoutVerifier.Verify(Layout(0, Box(20,1))));
        Assert.Throws<InvalidOperationException>(() => ShelfLayoutVerifier.Verify(Layout(0, Box(1,20))));
        Assert.Throws<InvalidOperationException>(() => ShelfLayoutVerifier.Verify(Layout(0, Box(double.NaN,1))));
    }

    [Fact]
    public void ExactComparisonDoesNotRoundShortClearanceUp()
    {
        // The rounded subtraction 1 - 2^-54 equals 1, but the exact gap is shorter.
        var tiny = Math.ScaleB(1d, -54);
        var first = Box(0, 1, tiny, 1);
        var second = Box(1, 1, 1, 1);
        var result = new NestingResult(new ProjectDocument { Pieces = [first, second] }, 10, 0, 0)
            { FabricWidth = 10, Gap = 1, EdgeMargin = 0 };
        Assert.Throws<InvalidOperationException>(() => ShelfLayoutVerifier.Verify(result));
    }

    [Fact]
    public void RejectsWorkBeyondPairBudgetAndHonorsCancellation()
    {
        var many = Enumerable.Range(0, 2001).Select(_ => Box(1,1)).ToArray();
        Assert.Throws<InvalidOperationException>(() => ShelfLayoutVerifier.Verify(Layout(0, many)));
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => ShelfLayoutVerifier.Verify(Layout(0), source.Token));
    }
}

