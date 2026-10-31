using System.Reflection;
using Clipper2Lib;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class OffsetLocalGuardRegressionTests
{
    // Guard is tested directly: a whole-contour area budget can hide this notch.
    // Coordinates are integer grid units, as in PatternTools.Offset.
    [Theory]
    [InlineData(0L)]
    [InlineData(20000000000000L)]
    public void LostNotchIsRejectedByLocalComparisonRegardlessOfOrigin(long origin)
    {
        Point64 P(long x, long y) => new(origin + x, origin + y);
        var source = new Path64 { P(0,0), P(50000000000000,0), P(50000000000000,1000000000),
            P(25000002000000,1000000000), P(25000002000000,920000000),
            P(24999998000000,920000000), P(24999998000000,1000000000), P(0,1000000000) };
        var flattened = new Path64 { P(0,0), P(50000000000000,0), P(50000000000000,1000000000), P(0,1000000000) };
        var method = typeof(PatternTools).GetMethod("LocallyMatches", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        bool Matches(Path64 a, Path64 b) => (bool)method!.Invoke(null, [a,b,8.0])!;
        Assert.True(Matches(source,source));
        Assert.False(Matches(source,flattened));
    }
}
