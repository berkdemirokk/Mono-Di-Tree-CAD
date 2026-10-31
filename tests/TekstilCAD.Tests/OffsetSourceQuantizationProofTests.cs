using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public class OffsetSourceQuantizationProofTests
{
    private static bool Check(Point2[] source, GridPoint[] grid)
    {
        var type = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetSourceQuantizationProof", true)!;
        return (bool)type.GetMethod("TryValidate", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { source, grid, CancellationToken.None })!;
    }

    [Fact]
    public void AcceptsExactGridAndRejectsOneGridUnitDisplacement()
    {
        Point2[] source = [new(0, 0), new(1, 0), new(0, 1)];
        GridPoint[] grid = [new(0, 0), new(1_000_000, 0), new(0, 1_000_000)];
        Assert.True(Check(source, grid));
        grid[1] = new(1_000_001, 0);
        Assert.False(Check(source, grid));
    }

    [Fact]
    public void ChecksActualDoubleInsteadOfRoundedMultiplication()
    {
        // The binary64 value 0.0000005 is slightly below the exact half-grid.
        // Floating multiplication rounds it to 0.5 and production rounding
        // therefore chooses 1, which is more than half a grid from the input.
        Point2[] source = [new(0.0000005, 0), new(1, 0), new(0, 1)];
        GridPoint[] grid = [new(1, 0), new(1_000_000, 0), new(0, 1_000_000)];
        Assert.False(Check(source, grid));
        grid[0] = new(0, 0);
        Assert.True(Check(source, grid));
    }
}
