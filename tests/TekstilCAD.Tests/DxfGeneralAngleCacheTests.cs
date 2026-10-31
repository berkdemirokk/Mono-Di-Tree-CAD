using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfGeneralAngleCacheTests
{
    private static DxfGeneralPrecisionTransform Transform(string degrees) =>
        new("0", "0", "0", "0", "1", "1", degrees);

    [Fact]
    public void EquivalentAnglesShareOneCalculationAcrossPreparedOccurrences()
    {
        var cache = new DxfGeneralInsertPrecision.AngleCache(2);
        foreach (var angle in new[] { "30", "30.0", "3e1", "390", "-330" })
        {
            var result = DxfGeneralInsertPrecision.Prepare([Transform(angle)], "1", cache);
            Assert.True(result.IsWithinTolerance("2", "0", 1.7320508075688772, 1, 1e-12));
        }
        Assert.Equal(1, cache.Count);
        Assert.Equal(1, cache.Computations);
        var cardinal = DxfGeneralInsertPrecision.Prepare([Transform("90")], "1", cache);
        Assert.True(cardinal.IsWithinTolerance("2", "0", 0, 2, 0));
        Assert.Equal(2, cache.Count);
        Assert.Equal(2, cache.Computations);
    }

    [Fact]
    public void DistinctAnglesRespectTheBoundWithoutEvictingExistingResults()
    {
        var cache = new DxfGeneralInsertPrecision.AngleCache(1);
        var first = DxfGeneralInsertPrecision.Prepare([Transform("30")], "1", cache);
        Assert.Throws<InvalidDataException>(() => DxfGeneralInsertPrecision.Prepare([Transform("45")], "1", cache));
        Assert.Equal(1, cache.Count);
        Assert.Equal(1, cache.Computations);
        Assert.True(first.IsWithinTolerance("2", "0", 1.7320508075688772, 1, 1e-12));
        Assert.True(DxfGeneralInsertPrecision.Prepare([Transform("390")], "1", cache)
            .IsWithinTolerance("2", "0", 1.7320508075688772, 1, 1e-12));
        Assert.Equal(1, cache.Computations);
    }

    [Fact]
    public void SeparateCachesDoNotShareGlobalState()
    {
        var first = new DxfGeneralInsertPrecision.AngleCache(1);
        var second = new DxfGeneralInsertPrecision.AngleCache(1);
        _ = DxfGeneralInsertPrecision.Prepare([Transform("45")], "1", first);
        Assert.Equal(0, second.Count);
        Assert.Equal(0, second.Computations);
        _ = DxfGeneralInsertPrecision.Prepare([Transform("45")], "1", second);
        Assert.Equal(1, first.Computations);
        Assert.Equal(1, second.Computations);
    }

    [Fact]
    public void InvalidBoundsAndCancellationAreRejectedWithoutPopulatingCache()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DxfGeneralInsertPrecision.AngleCache(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DxfGeneralInsertPrecision.AngleCache(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DxfGeneralInsertPrecision.AngleCache(4097));
        var cache = new DxfGeneralInsertPrecision.AngleCache(2);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            DxfGeneralInsertPrecision.Prepare([Transform("30")], "1", cache, cancellation.Token));
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Computations);
        Assert.Throws<InvalidDataException>(() => DxfGeneralInsertPrecision.Prepare([Transform("not-a-number")], "1", cache));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void OutOfRangeAngleIsRejectedEvenWhenEquivalentZeroIsAlreadyCached()
    {
        // All three tokens reduce mathematically to zero. Their raw exponent
        // or decimal power must still satisfy the trig parser's work limits.
        var invalidAngles = new[] { "30e129", "-30e129", "0." + new string('0', 129) + "e-128" };
        foreach (var invalid in invalidAngles)
        {
            var warm = new DxfGeneralInsertPrecision.AngleCache(2);
            _ = DxfGeneralInsertPrecision.Prepare([Transform("0")], "1", warm);
            Assert.Throws<InvalidDataException>(() => DxfGeneralInsertPrecision.Prepare([Transform(invalid)], "1", warm));
            Assert.Equal(1, warm.Count);
            Assert.Equal(1, warm.Computations);
            // A valid equivalent remains reusable after the rejection.
            _ = DxfGeneralInsertPrecision.Prepare([Transform("0e128")], "1", warm);
            Assert.Equal(1, warm.Computations);

            var cold = new DxfGeneralInsertPrecision.AngleCache(2);
            Assert.Throws<InvalidDataException>(() => DxfGeneralInsertPrecision.Prepare([Transform(invalid)], "1", cold));
            Assert.Equal(0, cold.Count);
            _ = DxfGeneralInsertPrecision.Prepare([Transform("0")], "1", cold);
            Assert.Equal(1, cold.Count);
            Assert.Equal(1, cold.Computations);
        }
    }

    [Theory]
    [InlineData("30", 1.7320508075688772, 1.0)]
    [InlineData("90", 0.0, 2.0)]
    [InlineData("-45", 1.4142135623730951, -1.4142135623730951)]
    public void CachedAndFreshContextsPreserveNumericalAcceptance(string degrees, double x, double y)
    {
        var cache = new DxfGeneralInsertPrecision.AngleCache(2);
        var cached = DxfGeneralInsertPrecision.Prepare([Transform(degrees)], "1", cache);
        var fresh = DxfGeneralInsertPrecision.Prepare([Transform(degrees)], "1");
        foreach (var offset in new[] { 0d, 1e-13, .01 })
            Assert.Equal(fresh.IsWithinTolerance("2", "0", x + offset, y, 1e-12),
                cached.IsWithinTolerance("2", "0", x + offset, y, 1e-12));
    }

    [Theory]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public void ImportPreparationLimitStillAppliesWithRepeatedAngles(int count, bool accepted)
    {
        const string table = "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n" +
            "0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n0\nENDTAB\n0\nENDSEC\n";
        const string block = "0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n10\n0\n20\n0\n30\n0\n" +
            "0\nLWPOLYLINE\n8\n0\n90\n3\n70\n1\n10\n0\n20\n0\n10\n2\n20\n0\n10\n0\n20\n1\n" +
            "0\nENDBLK\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n";
        var builder = new StringBuilder(table).Append(block);
        for (int i = 0; i < count; i++)
            builder.Append("0\nINSERT\n2\nA\n8\n0\n10\n").Append(i * 10).Append("\n20\n0\n50\n30\n");
        var bytes = Encoding.UTF8.GetBytes(builder.Append("0\nENDSEC\n0\nEOF\n").ToString());
        using var input = new MemoryStream(bytes, writable: false);
        if (accepted)
        {
            var result = DxfFormat.ImportWithBlockPlacements(input, "cache test", 1,
                DxfTextEncoding.Utf8, 1e-9, ["0"]);
            Assert.Equal(count, result.Project.Pieces.Count);
        }
        else Assert.Throws<InvalidDataException>(() => DxfFormat.ImportWithBlockPlacements(input, "cache test", 1,
            DxfTextEncoding.Utf8, 1e-9, ["0"]));
        Assert.Equal(bytes, input.ToArray());
    }
}
