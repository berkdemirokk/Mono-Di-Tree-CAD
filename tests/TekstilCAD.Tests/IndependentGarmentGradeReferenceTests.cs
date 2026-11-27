using TekstilCAD.Core;
using TekstilCAD.Grading;

namespace TekstilCAD.Tests;

/// <summary>
/// Arithmetic cross-check against Open+Pattern's published Standard Fit T-Shirt size step.
/// The contours here are synthetic half-body test pieces, not copied pattern geometry.
/// </summary>
public sealed class IndependentGarmentGradeReferenceTests
{
    [Fact]
    public void FrontAndBackHalfBodyGradePreservesPublishedSixCentimeterChestStep()
    {
        // Open+Pattern publishes a 6 cm increment between graded sizes. A half-body
        // front and back each contribute 15 mm per cut half; mirrored across the fold,
        // the whole chest circumference grows by 60 mm.
        var front = ChestHalf("Front");
        var back = ChestHalf("Back");
        Point2[] deltas = [new(0, 0), new(15, 0), new(15, 0), new(0, 0)];

        var gradedFront = GradingEngine.Generate(front, deltas, 1, "Next size");
        var gradedBack = GradingEngine.Generate(back, deltas, 1, "Next size");

        var chestStep = 2 * (HorizontalChestWidth(gradedFront) - HorizontalChestWidth(front))
            + 2 * (HorizontalChestWidth(gradedBack) - HorizontalChestWidth(back));
        Assert.Equal(60, chestStep, precision: 6);
        Assert.Equal(300, HorizontalChestWidth(front), precision: 6);
        Assert.Equal(300, HorizontalChestWidth(back), precision: 6);
    }

    private static PatternPiece ChestHalf(string name) => new()
    {
        Name = name,
        Closed = true,
        Points = [new(0, 0), new(300, 0), new(300, 400), new(0, 400)]
    };

    private static double HorizontalChestWidth(PatternPiece piece) => piece.Points[1].X - piece.Points[0].X;
}
