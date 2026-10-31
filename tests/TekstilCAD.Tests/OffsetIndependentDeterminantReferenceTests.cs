using System.Numerics;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Exact independent line-intersection checks; not production approval.</summary>
public sealed class OffsetIndependentDeterminantReferenceTests
{
    [Theory]
    [InlineData(false, 0L, 0L)]
    [InlineData(true, 0L, 0L)]
    [InlineData(false, 50000000000000L, -50000000000000L)]
    [InlineData(true, -50000000000000L, 50000000000000L)]
    public void RationalMiterSatisfiesTwoIndependentlyShiftedLines(bool reverse, long ox, long oy)
    {
        // Edges (3,4) and (5,12) have lengths 5 and 13. For delta 130,
        // shifted lines are 4x-3y=650 and 12x-5y=1690 relative to b.
        // Their determinant solution is (455/4,-65), without a bisector formula.
        var a = new GridPoint(ox - 3, oy - 4);
        var b = new GridPoint(ox, oy);
        var c = new GridPoint(ox + 5, oy + 12);
        if (reverse) (a, c) = (c, a);
        Assert.True(TryBound(a, b, c, 130, reverse ? -1 : 1, out var box));
        BigInteger exactXTimesFour = ((BigInteger)ox * 4 + 455) * Scale;
        BigInteger exactY = ((BigInteger)oy - 65) * Scale;
        Assert.True(4 * box.X.Lower <= exactXTimesFour && exactXTimesFour <= 4 * box.X.Upper);
        Assert.True(box.Y.Lower <= exactY && exactY <= box.Y.Upper);
        Assert.True(box.X.Upper - box.X.Lower < Scale);
        Assert.True(box.Y.Upper - box.Y.Lower < Scale);
    }
}
