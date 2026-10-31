using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class OffsetCornerJoinAdversarialTests
{
    private static List<Point2> Source(double o) =>
        [new(o,o), new(o+10,o), new(o+10,o+4), new(o+4,o+4), new(o+4,o+10), new(o,o+10)];

    private static List<Point2> Ideal(double o) =>
        [new(o-1,o-1), new(o+11,o-1), new(o+11,o+5), new(o+5,o+5), new(o+5,o+11), new(o-1,o+11)];

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(50000000.0, true)]
    public void EquivalentConcaveCornerWithSplitSegmentsKeepsCyclicOrder(double o, bool reverse)
    {
        var source = Source(o); var target = Ideal(o);
        target.Insert(3, new Point2(o+8, o+5));
        target.Insert(5, new Point2(o+5, o+8));
        if (reverse) target.Reverse();
        var before = source.ToArray();
        var result = OffsetOrderedDiagnostic.Compare(source, 1, target, .00001);
        Assert.True(result.CandidateConstructed, result.Reason);
        Assert.True(result.OrderedMatch, result.Reason);
        Assert.Equal(before, source);
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(50000000.0, true)]
    public void AddedSmallNotchAboveCorridorCannotBeMatched(double o, bool reverse)
    {
        var target = Ideal(o);
        target.InsertRange(3, [new(o+8.01,o+5), new(o+8,o+4.995), new(o+7.99,o+5)]);
        if (reverse) target.Reverse();
        Geometry.ValidateContour(target);
        var result = OffsetOrderedDiagnostic.Compare(Source(o), 1, target, .001);
        Assert.False(result.OrderedMatch);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(50000000.0)]
    public void CornerMovedBeyondCorridorCannotBeMatched(double o)
    {
        var target = Ideal(o); target[3] = new Point2(o+5.01,o+5.01);
        var result = OffsetOrderedDiagnostic.Compare(Source(o), 1, target, .001);
        Assert.False(result.OrderedMatch);
    }

    [Fact]
    public void NeighboringCornersInsideCorridorCannotBeCertified()
    {
        List<Point2> source = [new(0,0), new(.1,0), new(.1,.1), new(0,.1)];
        List<Point2> target = [new(-.01,-.01), new(.11,-.01), new(.11,.11), new(-.01,.11)];
        var result = OffsetOrderedDiagnostic.Compare(source, .01, target, .1);
        Assert.False(result.OrderedMatch);
        Assert.True(result.Ambiguous, result.Reason);
    }
}
