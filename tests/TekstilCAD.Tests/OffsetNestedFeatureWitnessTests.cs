using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>
/// Independent exact arithmetic witnesses for indexed subchains. This does not
/// discover features or prove that indices on an offset correspond to the source.
/// </summary>
public sealed class OffsetNestedFeatureWitnessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnOuterDepthWitnessCanMissLossOfAnInnerNotch(bool reverse)
    {
        // The local V lies inside the broad recess between the top corners.
        GridPoint[] source = [new(0,0), new(10,0), new(10,10), new(6,6),
            new(5,3), new(4,6), new(0,10)];
        GridPoint[] altered = [new(0,0), new(10,0), new(10,10), new(6,6),
            new(5,4), new(4,6), new(0,10)];
        if (reverse) { Array.Reverse(source); Array.Reverse(altered); }
        int Index(GridPoint point) => Array.IndexOf(source, point);
        int left = Index(new(0,10)), right = Index(new(10,10));
        int innerLeft = Index(new(4,6)), innerRight = Index(new(6,6));
        int apex = Index(new(5,3));

        // Both tests demand at least three quarters of the original depth.
        Assert.True(AtLeastFractionOfDepth(source, altered, left, apex, right, 3, 4));
        Assert.False(AtLeastFractionOfDepth(source, altered, innerLeft, apex, innerRight, 3, 4));
        Assert.True(AtLeastFractionOfDepth(source, source, innerLeft, apex, innerRight, 3, 4));
    }

    // Exact distance to an infinite chord, without square roots. A zero chord or
    // a moved witness across the chord is ambiguous and is never accepted.
    private static bool AtLeastFractionOfDepth(GridPoint[] source, GridPoint[] candidate,
        int first, int witness, int last, int numerator, int denominator)
    {
        static (BigInteger Cross, BigInteger LengthSquared) Measure(GridPoint[] p, int a, int b, int c)
        {
            BigInteger dx = (BigInteger)p[c].X - p[a].X, dy = (BigInteger)p[c].Y - p[a].Y;
            BigInteger wx = (BigInteger)p[b].X - p[a].X, wy = (BigInteger)p[b].Y - p[a].Y;
            return (dx * wy - dy * wx, dx * dx + dy * dy);
        }
        var s = Measure(source, first, witness, last);
        var c = Measure(candidate, first, witness, last);
        return s.LengthSquared > 0 && c.LengthSquared > 0 && s.Cross.Sign != 0 &&
            s.Cross.Sign == c.Cross.Sign &&
            c.Cross * c.Cross * s.LengthSquared * denominator * denominator >=
            s.Cross * s.Cross * c.LengthSquared * numerator * numerator;
    }
}
