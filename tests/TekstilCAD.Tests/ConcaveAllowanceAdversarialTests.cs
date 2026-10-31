using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class ConcaveAllowanceAdversarialTests
{
    // The notch is only 4 mm wide and 80 mm deep, while the outside perimeter
    // is ~100,000,000 mm. A perimeter-scaled global XOR tolerance can hide its loss.
    [Theory]
    [InlineData(0d, 0d, false)]
    [InlineData(-20000000d, -1000d, false)]
    [InlineData(-20000000d, -1000d, true)]
    public void LargePerimeterMustNotHideCollapsedNarrowRecess(double dx, double dy, bool reverse)
    {
        Point2 P(double x, double y) => new(x + dx, y + dy);
        var source = new PatternPiece { Name = "Narrow recess", Points =
        [P(0,0),P(50000000,0),P(50000000,1000),P(25000002,1000),
         P(25000002,920),P(24999998,920),P(24999998,1000),P(0,1000)] };
        if (reverse) source.Points.Reverse();
        var original = source.Points.ToArray();
        // A 3 mm outward offset would join across the 4 mm entrance. The operation
        // must reject rather than silently eliminate the original indentation.
        Assert.ThrowsAny<ArgumentException>(() => PatternTools.Offset(source, 3));
        Assert.Equal(original, source.Points);
    }
}
