using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>Independent checks of reuse and isolation for prepared placement checks.</summary>
public sealed class DxfGeneralPreparedPrecisionTests
{
    private static DxfGeneralPrecisionTransform T(string angle, string bx = "0", string by = "0",
        string ix = "0", string iy = "0", string sx = "1", string sy = "1") =>
        new(bx, by, ix, iy, sx, sy, angle);

    [Fact]
    public void PreparedNestedTransformsMatchFreshChecksAndIndependentReference()
    {
        DxfGeneralPrecisionTransform[] chain = [
            T("30", bx: "1", by: "2", ix: "10", iy: "20", sx: "-2", sy: "3"),
            T("90", ix: "100", iy: "200")];
        var prepared = DxfGeneralInsertPrecision.Prepare(chain, "2.5");
        const double expectedX = 192.00961894323342, expectedY = 508.8397459621556;
        Assert.True(prepared.IsWithinTolerance("3", "4", expectedX, expectedY, 1e-10));
        Assert.False(prepared.IsWithinTolerance("3", "4", expectedX, expectedY + .01, 1e-5));
        foreach (var offset in new[] { 0d, 1e-13, 1e-7, .01 })
            Assert.Equal(DxfGeneralInsertPrecision.IsWithinTolerance("3", "4", chain, "2.5", expectedX + offset, expectedY, 1e-10),
                prepared.IsWithinTolerance("3", "4", expectedX + offset, expectedY, 1e-10));
    }

    [Fact]
    public void MutatingTheInputArrayCannotChangePreparedTransforms()
    {
        DxfGeneralPrecisionTransform[] chain = [T("90")];
        var prepared = DxfGeneralInsertPrecision.Prepare(chain, "1");
        chain[0] = T("180", ix: "100");
        Assert.True(prepared.IsWithinTolerance("1", "2", -2, 1, 0));
        Assert.False(prepared.IsWithinTolerance("1", "2", 99, -2, 0));
    }

    [Fact]
    public async Task OnePreparedContextCanBeReadConcurrently()
    {
        var prepared = DxfGeneralInsertPrecision.Prepare([T("30"), T("45")], "1");
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(i => Task.Run(() =>
            prepared.IsWithinTolerance("2", "0", .5176380902050415,
                i % 2 == 0 ? 1.9318516525781366 : 2.1, 1e-12))));
        for (int i = 0; i < results.Length; i++) Assert.Equal(i % 2 == 0, results[i]);
    }

    [Fact]
    public void PreparationEnforcesWorkLimitsAndInvalidInputs()
    {
        Assert.Throws<ArgumentNullException>(() => DxfGeneralInsertPrecision.Prepare(null!, "1"));
        Assert.Throws<InvalidDataException>(() => DxfGeneralInsertPrecision.Prepare(Enumerable.Repeat(T("30"), 65).ToArray(), "1"));
        Assert.Throws<InvalidDataException>(() => DxfGeneralInsertPrecision.Prepare([T("30", sx: "0")], "1"));
        Assert.Throws<InvalidDataException>(() => DxfGeneralInsertPrecision.Prepare([T("30")], "0"));
        Assert.Throws<InvalidDataException>(() => DxfGeneralInsertPrecision.Prepare([T("invalid")], "1"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => DxfGeneralInsertPrecision.Prepare([T("45")], "1", cancellation.Token));
    }

    [Fact]
    public void EachPreparedCheckValidatesItsInputsAndCancellation()
    {
        var prepared = DxfGeneralInsertPrecision.Prepare([T("90")], "1");
        Assert.Throws<InvalidDataException>(() => prepared.IsWithinTolerance("1e257", "0", 0, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => prepared.IsWithinTolerance("1", "0", double.NaN, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => prepared.IsWithinTolerance("1", "0", 0, 1, -1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => prepared.IsWithinTolerance("1", "0", 0, 1, 0, cancellation.Token));
    }

    [Fact]
    public void FullDepthCardinalAndNoncardinalChainsRemainBounded()
    {
        var cardinal = DxfGeneralInsertPrecision.Prepare(Enumerable.Repeat(T("90"), 64).ToArray(), "1");
        Assert.True(cardinal.IsWithinTolerance("1", "2", 1, 2, 0));
        var noncardinal = DxfGeneralInsertPrecision.Prepare(Enumerable.Repeat(T("45"), 64).ToArray(), "1");
        Assert.True(noncardinal.IsWithinTolerance("1", "0", 1, 0, 1e-9));
    }
}
