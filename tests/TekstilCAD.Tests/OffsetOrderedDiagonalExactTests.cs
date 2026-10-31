using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class OffsetOrderedDiagonalExactTests
{
    // A 45-degree diamond with sqrt(2) allowance has integral miter vertices.
    // The inserted midpoint is exactly collinear in binary64 even at this origin.
    private static List<Point2> Source(double o) =>
        [new(o,o), new(o+100,o+100), new(o,o+200), new(o-100,o+100)];
    private static List<Point2> SplitOutput(double o, double perturbation) =>
        [new(o,o-2), new(o+51,o+49+perturbation), new(o+102,o+100),
         new(o,o+202), new(o-102,o+100)];

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(0.0, true)]
    [InlineData(50000000.0, false)]
    [InlineData(50000000.0, true)]
    public void ExactDiagonalSubdivisionMatchesWithoutChangingInput(double origin, bool reverse)
    {
        var source=Source(origin); var output=SplitOutput(origin,0);
        if(reverse) { source.Reverse(); output.Reverse(); }
        Geometry.ValidateContour(source); Geometry.ValidateContour(output);
        var originalSource=source.ToArray(); var originalOutput=output.ToArray();
        var result=OffsetOrderedDiagnostic.Compare(source,Math.Sqrt(2),output,.01);
        Assert.True(result.CandidateConstructed,result.Reason);
        Assert.True(result.OrderedMatch,result.Reason);
        Assert.False(result.Ambiguous);
        Assert.Equal(originalSource,source); Assert.Equal(originalOutput,output);
    }

    [Theory]
    [InlineData(0.0, false, 0.005)]
    [InlineData(0.0, true, -0.000005)]
    [InlineData(50000000.0, false, 0.000005)]
    [InlineData(50000000.0, true, -0.005)]
    public void SlightlyBrokenDiagonalIsNotCertified(double origin, bool reverse, double perturbation)
    {
        var source=Source(origin); var output=SplitOutput(origin,perturbation);
        if(reverse) { source.Reverse(); output.Reverse(); }
        Geometry.ValidateContour(source); Geometry.ValidateContour(output);
        var originalSource=source.ToArray(); var originalOutput=output.ToArray();
        var result=OffsetOrderedDiagnostic.Compare(source,Math.Sqrt(2),output,.01);
        Assert.False(result.OrderedMatch,"A real deviation inside the distance corridor must not be certified as a straight subdivision.");
        Assert.Equal(originalSource,source); Assert.Equal(originalOutput,output);
    }
}
