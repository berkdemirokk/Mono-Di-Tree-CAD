using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>Independent numeric examples for decimal placements with non-cardinal angles.</summary>
public sealed class DxfGeneralInsertPrecisionTests
{
    private static DxfGeneralPrecisionTransform T(string angle, string bx = "0", string by = "0",
        string ix = "0", string iy = "0", string sx = "1", string sy = "1") =>
        new(bx, by, ix, iy, sx, sy, angle);

    [Theory]
    [InlineData("30", 1.7320508075688772935, 1.0)]
    [InlineData("45", 1.4142135623730950488, 1.4142135623730950488)]
    [InlineData("-315", 1.4142135623730950488, 1.4142135623730950488)]
    [InlineData("360000000000000000000000000030", 1.7320508075688772935, 1.0)]
    public void RotationMatchesIndependentHighPrecisionReference(string angle, double x, double y)
    {
        // 2*(cos(30), sin(30)) = (sqrt(3), 1);
        // 2*(cos(45), sin(45)) = (sqrt(2), sqrt(2)).
        Assert.True(DxfGeneralInsertPrecision.IsWithinTolerance("2", "0", [T(angle)], "1", x, y, 1e-12));
        Assert.False(DxfGeneralInsertPrecision.IsWithinTolerance("2", "0", [T(angle)], "1", x + 0.001, y, 1e-6));
    }

    [Fact]
    public void AngleNearNinetyIsNotRoundedToExactlyNinety()
    {
        // cos(89.999999999999999 degrees) is approximately 1.7453292519943296e-17.
        var chain = new[] { T("89.999999999999999") };
        Assert.False(DxfGeneralInsertPrecision.IsWithinTolerance("1", "0", chain, "1", 0, 1, 1e-18));
        Assert.True(DxfGeneralInsertPrecision.IsWithinTolerance("1", "0", chain, "1", 0, 1, 1e-15));
    }

    [Fact]
    public void NestedRotationReflectionNonuniformScaleAndUnitConversionAreInCorrectOrder()
    {
        DxfGeneralPrecisionTransform[] chain = [
            T("30", bx: "1", by: "2", ix: "10", iy: "20", sx: "-2", sy: "3"),
            T("90", ix: "100", iy: "200")];
        // Child = (7 - 2sqrt(3), 18 + 3sqrt(3));
        // parent+unit = ((82 - 3sqrt(3))*2.5, (207 - 2sqrt(3))*2.5).
        Assert.True(DxfGeneralInsertPrecision.IsWithinTolerance("3", "4", chain, "2.5",
            192.00961894323342, 508.8397459621556, 1e-10));
        Assert.False(DxfGeneralInsertPrecision.IsWithinTolerance("3", "4", chain, "2.5",
            192.00961894323342, 508.84, 1e-5));
    }

    [Fact]
    public void TwoNonCardinalNestedAnglesComposeToSeventyFiveDegrees()
    {
        // R45(R30((2,0))) = 2*(cos 75°, sin 75°).
        DxfGeneralPrecisionTransform[] chain = [T("30"), T("45")];
        Assert.True(DxfGeneralInsertPrecision.IsWithinTolerance("2", "0", chain, "1",
            0.5176380902050415, 1.9318516525781366, 1e-12));
        Assert.False(DxfGeneralInsertPrecision.IsWithinTolerance("2", "0", chain, "1",
            0.5176380902050415, 1.9319516525781366, 1e-6));
    }

    [Fact]
    public void TwoNonCardinalTransformsWithAnisotropicReflectionUseChildFirst()
    {
        DxfGeneralPrecisionTransform[] chain = [T("30", sx: "-2", sy: "3"), T("45", sx: "-1", sy: "2")];
        // Child maps (1,0) to (-sqrt(3), -1); parent scales to (sqrt(3),-2),
        // then rotates to ((sqrt(3)+2)/sqrt(2), (sqrt(3)-2)/sqrt(2)).
        Assert.True(DxfGeneralInsertPrecision.IsWithinTolerance("1", "0", chain, "1",
            2.6389584337646843, -0.189468690981506, 1e-12));
        Assert.False(DxfGeneralInsertPrecision.IsWithinTolerance("1", "0", chain, "1",
            2.6389584337646843, 0.189468690981506, 1e-6));
    }

    [Fact]
    public void SixtyFourExactQuarterTurnsFitTheDocumentedDepthLimit()
    {
        var chain = Enumerable.Repeat(T("90"), 64).ToArray();
        Assert.True(DxfGeneralInsertPrecision.IsWithinTolerance("1", "2", chain, "1", 1, 2, 0));
    }

    [Fact]
    public void SixtyFourNonCardinalTurnsDoNotLoseTheExpectedIdentity()
    {
        // Eight full turns, with interval widening contained by the requested tolerance.
        // This exercises the full declared depth; an arithmetic-work-limit rejection
        // exposes a practical compatibility limitation and must not be hidden.
        var chain = Enumerable.Repeat(T("45"), 64).ToArray();
        Assert.True(DxfGeneralInsertPrecision.IsWithinTolerance("1", "0", chain, "1", 1, 0, 1e-9));
    }

    [Fact]
    public void ToleranceIsPerComponentAndBoundaryIsInclusive()
    {
        Assert.True(DxfGeneralInsertPrecision.IsWithinTolerance("1", "-1", [], "1", 0, 0, 1));
        Assert.False(DxfGeneralInsertPrecision.IsWithinTolerance("1", "-1", [], "1", 0, 0, 0.5));
        Assert.True(DxfGeneralInsertPrecision.IsWithinTolerance("1", "2", [T("90")], "1", -2, 1, 0));
        Assert.False(DxfGeneralInsertPrecision.IsWithinTolerance("1", "0", [T("45")], "1",
            0.7071067811865476, 0.7071067811865476, 0));
    }

    [Fact]
    public void RejectsInvalidValuesAndExcessiveWorkAndHonorsCancellation()
    {
        Assert.Throws<InvalidDataException>(() => DxfGeneralInsertPrecision.IsWithinTolerance("1", "0", [T("30", sx: "0")], "1", 0, 0, 1));
        Assert.Throws<InvalidDataException>(() => DxfGeneralInsertPrecision.IsWithinTolerance("1", "0", [T("30")], "0", 0, 0, 1));
        Assert.Throws<InvalidDataException>(() => DxfGeneralInsertPrecision.IsWithinTolerance("1", "0", Enumerable.Repeat(T("30"), 65).ToArray(), "1", 0, 0, 1));
        Assert.Throws<InvalidDataException>(() => DxfGeneralInsertPrecision.IsWithinTolerance("1e257", "0", [], "1", 0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DxfGeneralInsertPrecision.IsWithinTolerance("1", "0", [], "1", double.NaN, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DxfGeneralInsertPrecision.IsWithinTolerance("1", "0", [], "1", 0, 0, -1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => DxfGeneralInsertPrecision.IsWithinTolerance("1", "0", [T("30")], "1", 0, 0, 1, cancellation.Token));
    }
}
