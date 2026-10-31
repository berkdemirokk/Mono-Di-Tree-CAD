using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

// This is an adversarial test of the experimental diagnostic only. Passing it
// would not establish a production safety certificate or a numeric error bound.
public class ShadowShallowNotchTests
{
    [Theory]
    [InlineData(false, 0.0)]
    [InlineData(true, 0.0)]
    [InlineData(false, 50000000.0)]
    [InlineData(true, 50000000.0)]
    public void ExtraShallowNotchIsNotCertifiedAsTheExpectedOffset(bool reverse, double origin)
    {
        List<Point2> source = [new(origin, origin), new(origin + 100, origin),
            new(origin + 100, origin + 100), new(origin, origin + 100)];
        // The expected offset of this square has a straight top edge. This output
        // instead contains a shallow V, entirely within the comparison tolerance;
        // neither of its two new edges makes a sharp turn under the current veto.
        List<Point2> altered = [new(origin - 1, origin - 1), new(origin + 101, origin - 1),
            new(origin + 101, origin + 101), new(origin + 50.01, origin + 101),
            new(origin + 50, origin + 100.999), new(origin + 49.99, origin + 101),
            new(origin - 1, origin + 101)];
        if (reverse) { source.Reverse(); altered.Reverse(); }

        var result = OffsetShadowDiagnostic.Compare(source, 1, altered, .02);

        Assert.True(result.Compared);
        Assert.False(result.FullyCovered, "The altered contour contains a shallow extra notch.");
    }
}
